using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using InkTag.Core;
using InkTag.Core.Logging;

namespace InkTag.Mcp;

internal class Program
{
    private static readonly MetadataEditor _editor = new();

    private static void Main(string[] args)
    {
        // Redirect standard Console.Out to Console.Error to protect stdout for stdio JSON-RPC stream
        Console.SetOut(Console.Error);
        AppLogger.Initialize();

        // Stdio communication line-by-line
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();
        using var reader = new StreamReader(stdin);
        using var writer = new StreamWriter(stdout) { AutoFlush = true };

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonElement id = default;
            bool hasId = false;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (!root.TryGetProperty("method", out var methodProp))
                {
                    continue;
                }

                string method = methodProp.GetString() ?? "";
                hasId = root.TryGetProperty("id", out id);

                switch (method)
                {
                    case "initialize":
                        if (hasId) SendResponse(writer, id, GetInitializeResult());
                        break;
                    case "notifications/initialized":
                        // Notification acknowledgment, no response needed
                        break;
                    case "ping":
                        if (hasId) SendResponse(writer, id, new { });
                        break;
                    case "tools/list":
                        if (hasId) SendResponse(writer, id, GetToolsListResult());
                        break;
                    case "tools/call":
                        if (hasId && root.TryGetProperty("params", out var paramsElement))
                        {
                            var callResult = HandleToolCall(paramsElement);
                            SendResponse(writer, id, callResult);
                        }
                        break;
                    default:
                        if (hasId)
                        {
                            SendError(writer, id, -32601, $"Method '{method}' not found.");
                        }
                        break;
                }
            }
            catch (JsonException ex)
            {
                AppLogger.LogWarning($"MCP JSON parse error: {ex.Message}");
                if (hasId)
                {
                    SendError(writer, id, -32700, $"Parse error: {ex.Message}");
                }
                else
                {
                    var response = new
                    {
                        jsonrpc = "2.0",
                        error = new { code = -32700, message = $"Parse error: {ex.Message}" }
                    };
                    writer.WriteLine(JsonSerializer.Serialize(response));
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("MCP server internal error", ex);
                if (hasId)
                {
                    SendError(writer, id, -32603, $"Internal error: {ex.Message}");
                }
                else
                {
                    var response = new
                    {
                        jsonrpc = "2.0",
                        error = new { code = -32603, message = $"Internal error: {ex.Message}" }
                    };
                    writer.WriteLine(JsonSerializer.Serialize(response));
                }
            }
        }
    }

    private static object GetInitializeResult()
    {
        return new
        {
            protocolVersion = "2024-11-05",
            capabilities = new
            {
                tools = new { }
            },
            serverInfo = new
            {
                name = "InkTag.Mcp",
                version = "1.0.0"
            }
        };
    }

    private static object GetToolsListResult()
    {
        return new
        {
            tools = new object[]
            {
                new
                {
                    name = "read_comic_metadata",
                    description = "Reads XML metadata embedded in a CBZ or CBR archive and returns it as JSON.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            path = new { type = "string", description = "Path to comic archive (.cbz / .cbr)" }
                        },
                        required = new[] { "path" }
                    }
                },
                new
                {
                    name = "update_comic_metadata",
                    description = "Updates metadata properties in a comic archive or directory using a JSON patch.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            path = new { type = "string", description = "Target file or directory path" },
                            patch = new { type = "object", description = "Key-value property updates (e.g. {\"Writer\": \"Stan Lee\"})" },
                            dryRun = new { type = "boolean", description = "If true, previews diffs without modifying files on disk." }
                        },
                        required = new[] { "path", "patch" }
                    }
                },
                new
                {
                    name = "extract_cover_image",
                    description = "Extracts front cover art from a comic archive for multimodal vision inspection.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            path = new { type = "string", description = "Path to comic archive (.cbz / .cbr)" },
                            outputPath = new { type = "string", description = "Optional destination file path for image" },
                            returnBase64 = new { type = "boolean", description = "If true, returns base64 encoded image bytes" }
                        },
                        required = new[] { "path" }
                    }
                },
                new
                {
                    name = "scan_comics",
                    description = "Scans a directory for comic archives and checks for missing metadata fields.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            directory = new { type = "string", description = "Directory path to scan" },
                            missingFields = new
                            {
                                type = "array",
                                items = new { type = "string" },
                                description = "Fields to flag if null/empty (e.g. [\"Writer\", \"Series\"])"
                            }
                        },
                        required = new[] { "directory" }
                    }
                },
                new
                {
                    name = "get_comic_schema",
                    description = "Returns the JSON Schema specification for valid ComicInfo metadata properties.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new { }
                    }
                }
            }
        };
    }

    private static object HandleToolCall(JsonElement paramsElement)
    {
        string toolName = paramsElement.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
        JsonElement args = paramsElement.TryGetProperty("arguments", out var argsProp) ? argsProp : default;

        try
        {
            switch (toolName)
            {
                case "read_comic_metadata":
                    {
                        string path = args.GetProperty("path").GetString()!;
                        var metadata = _editor.ReadMetadata(path);
                        string json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
                        return FormatTextResult($"Metadata for {Path.GetFileName(path)}:\n{json}");
                    }

                case "update_comic_metadata":
                    {
                        string path = args.GetProperty("path").GetString()!;
                        string patchJson = args.GetProperty("patch").GetRawText();
                        bool dryRun = args.TryGetProperty("dryRun", out var dryProp) && dryProp.GetBoolean();

                        if (File.Exists(path))
                        {
                            var diffs = _editor.GetMetadataDiff(path, patchJson);
                            var warnings = MetadataEditor.ApplyJsonPatch(new ComicInfo(), patchJson);
                            if (!dryRun)
                            {
                                _editor.EditMetadataFromJson(path, patchJson);
                            }
                            object resObj = warnings.Count > 0
                                ? new { path, dryRun, modifiedFields = diffs.Count, diffs, warnings }
                                : new { path, dryRun, modifiedFields = diffs.Count, diffs };
                            string resJson = JsonSerializer.Serialize(resObj, new JsonSerializerOptions { WriteIndented = true });
                            return FormatTextResult(resJson);
                        }
                        else if (Directory.Exists(path))
                        {
                            if (dryRun)
                            {
                                var files = Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                                    .Where(f => f.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cbr", StringComparison.OrdinalIgnoreCase));
                                var fileDiffs = files.Select(f => new { path = f, diffs = _editor.GetMetadataDiff(f, patchJson) }).ToList();
                                return FormatTextResult(JsonSerializer.Serialize(new { dryRun = true, files = fileDiffs }, new JsonSerializerOptions { WriteIndented = true }));
                            }
                            else
                            {
                                var report = _editor.BulkEditMetadataFromJson(path, patchJson);
                                return FormatTextResult(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                            }
                        }
                        else
                        {
                            return FormatErrorResult($"Path not found: {path}");
                        }
                    }

                case "extract_cover_image":
                    {
                        string path = args.GetProperty("path").GetString()!;
                        string? outputPath = args.TryGetProperty("outputPath", out var outProp) ? outProp.GetString() : null;
                        bool returnBase64 = args.TryGetProperty("returnBase64", out var b64Prop) && b64Prop.GetBoolean();

                        if (string.IsNullOrEmpty(outputPath))
                        {
                            outputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_cover.jpg");
                        }

                        string? extracted = _editor.ExtractCoverImage(path, outputPath);
                        if (extracted == null || !File.Exists(extracted))
                        {
                            return FormatErrorResult("Failed to extract cover image.");
                        }

                        if (returnBase64)
                        {
                            byte[] bytes = File.ReadAllBytes(extracted);
                            string b64 = Convert.ToBase64String(bytes);
                            string mime = extracted.EndsWith(".png") ? "image/png" : "image/jpeg";

                            return new
                            {
                                content = new object[]
                                {
                                    new { type = "text", text = $"Cover extracted to {extracted}" },
                                    new
                                    {
                                        type = "image",
                                        data = b64,
                                        mimeType = mime
                                    }
                                }
                            };
                        }

                        return FormatTextResult($"Cover image extracted to: {extracted}");
                    }

                case "scan_comics":
                    {
                        string dir = args.GetProperty("directory").GetString()!;
                        var missingFields = new List<string>();
                        if (args.TryGetProperty("missingFields", out var mfElem) && mfElem.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in mfElem.EnumerateArray())
                            {
                                if (item.GetString() is string s) missingFields.Add(s);
                            }
                        }

                        var files = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                            .Where(f => f.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cbr", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        var items = files.Select(f =>
                        {
                            var meta = _editor.ReadMetadata(f);
                            var missing = new List<string>();
                            if (missingFields.Count > 0)
                            {
                                var props = typeof(ComicInfo).GetProperties();
                                foreach (var req in missingFields)
                                {
                                    var p = props.FirstOrDefault(pr => pr.Name.Equals(req, StringComparison.OrdinalIgnoreCase));
                                    if (p != null)
                                    {
                                        var val = p.GetValue(meta);
                                        if (val == null || (val is string str && string.IsNullOrWhiteSpace(str)))
                                        {
                                            missing.Add(p.Name);
                                        }
                                    }
                                }
                            }
                            return new { path = f, title = meta.Title, series = meta.Series, number = meta.Number, missing };
                        }).ToList();

                        string res = JsonSerializer.Serialize(new { directory = dir, totalFound = files.Count, comics = items }, new JsonSerializerOptions { WriteIndented = true });
                        return FormatTextResult(res);
                    }

                case "get_comic_schema":
                    {
                        string schema = MetadataEditor.ExportJsonSchema();
                        return FormatTextResult(schema);
                    }

                default:
                    return FormatErrorResult($"Unknown tool name: '{toolName}'");
            }
        }
        catch (Exception ex)
        {
            return FormatErrorResult($"Tool execution error: {ex.Message}");
        }
    }

    private static object FormatTextResult(string text)
    {
        return new
        {
            content = new[]
            {
                new { type = "text", text = text }
            }
        };
    }

    private static object FormatErrorResult(string message)
    {
        return new
        {
            isError = true,
            content = new[]
            {
                new { type = "text", text = message }
            }
        };
    }

    private static void SendResponse(StreamWriter writer, JsonElement id, object result)
    {
        var response = new
        {
            jsonrpc = "2.0",
            id = id,
            result = result
        };
        writer.WriteLine(JsonSerializer.Serialize(response));
    }

    private static void SendError(StreamWriter writer, JsonElement id, int code, string message)
    {
        var response = new
        {
            jsonrpc = "2.0",
            id = id,
            error = new { code, message }
        };
        writer.WriteLine(JsonSerializer.Serialize(response));
    }
}
