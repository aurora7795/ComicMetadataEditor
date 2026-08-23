using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using System.Xml.Serialization;
using InkTag.Core.Exceptions;
using InkTag.Core.Logging;

namespace InkTag.Core;

/// <summary>
/// Internal service responsible for ComicInfo XML sanitization, normalization, schema validation,
/// and high-performance serialization with cached XmlSerializer instances.
/// </summary>
internal static class ComicInfoXmlSanitizer
{
    private static readonly XmlSerializer ComicInfoSerializer = new(typeof(ComicInfo));

    /// <summary>
    /// Deserializes a ComicInfo XML stream into a ComicInfo model, applying sanitization and fallback parsing if necessary.
    /// </summary>
    public static ComicInfo DeserializeComicInfo(Stream stream)
    {
        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            string rawXml = reader.ReadToEnd();
            return DeserializeComicInfo(rawXml);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"Failed to read ComicInfo XML stream: {ex.Message}");
            return new ComicInfo();
        }
    }

    /// <summary>
    /// Deserializes a raw ComicInfo XML string into a ComicInfo model.
    /// </summary>
    public static ComicInfo DeserializeComicInfo(string rawXml)
    {
        if (string.IsNullOrWhiteSpace(rawXml))
        {
            return new ComicInfo();
        }

        try
        {
            string sanitizedXml = SanitizeComicInfoXml(rawXml);
            using var stringReader = new StringReader(sanitizedXml);
            using var xmlReader = XmlReader.Create(stringReader, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                IgnoreComments = true,
                IgnoreWhitespace = true,
                CheckCharacters = false
            });

            var result = (ComicInfo?)ComicInfoSerializer.Deserialize(xmlReader);
            return result ?? new ComicInfo();
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"Failed to deserialize ComicInfo XML ({ex.Message}). Attempting fallback parsing...");
            try
            {
                return FallbackParseComicInfoXml(rawXml);
            }
            catch (Exception fallbackEx)
            {
                AppLogger.LogWarning($"Fallback XML parser failed: {fallbackEx.Message}");
                return new ComicInfo();
            }
        }
    }

    /// <summary>
    /// Serializes a ComicInfo model into an output stream using UTF-8 encoding.
    /// </summary>
    public static void SerializeComicInfo(ComicInfo comicInfo, Stream outputStream)
    {
        ArgumentNullException.ThrowIfNull(comicInfo);
        ArgumentNullException.ThrowIfNull(outputStream);

        try
        {
            ComicInfoSerializer.Serialize(outputStream, comicInfo);
        }
        catch (Exception ex)
        {
            throw new MetadataXmlSanitizationException($"Failed to serialize ComicInfo to XML: {ex.Message}", innerException: ex);
        }
    }

    /// <summary>
    /// Sanitizes malformed ComicInfo XML: strips XML 1.0 control characters, cleans empty numeric tags,
    /// normalizes boolean strings and manga direction values.
    /// </summary>
    public static string SanitizeComicInfoXml(string rawXml)
    {
        if (string.IsNullOrWhiteSpace(rawXml))
            return "<ComicInfo />";

        // 1. Strip invalid XML 1.0 control characters (except \t, \n, \r)
        var sb = new StringBuilder(rawXml.Length);
        foreach (char c in rawXml)
        {
            if (c == 0x9 || c == 0xA || c == 0xD ||
                (c >= 0x20 && c <= 0xD7FF) ||
                (c >= 0xE000 && c <= 0xFFFD))
            {
                sb.Append(c);
            }
        }
        string cleaned = sb.ToString();

        try
        {
            // 2. Parse into XDocument for tree sanitization
            var xdoc = XDocument.Parse(cleaned, LoadOptions.PreserveWhitespace);
            var root = xdoc.Root;
            if (root == null) return cleaned;

            // Remove any xmlns declarations to prevent serialization namespace mismatch
            root.Attributes().Where(a => a.IsNamespaceDeclaration || a.Name.LocalName.StartsWith("xmlns", StringComparison.OrdinalIgnoreCase)).Remove();
            root.Name = "ComicInfo";

            var elementsToRemove = new List<XElement>();

            // List of numeric elements where empty or non-numeric inner text causes XmlSerializer format errors
            var integerElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Count", "Volume", "AlternateCount", "Year", "Month", "Day", "PageCount"
            };

            foreach (var elem in root.Elements())
            {
                string localName = elem.Name.LocalName;
                string val = elem.Value?.Trim() ?? "";

                if (integerElements.Contains(localName))
                {
                    if (string.IsNullOrEmpty(val) || !int.TryParse(val, out int intVal))
                    {
                        elementsToRemove.Add(elem);
                    }
                    else if (localName.Equals("Volume", StringComparison.OrdinalIgnoreCase))
                    {
                        if (intVal < 0) elementsToRemove.Add(elem);
                        else elem.Value = intVal.ToString();
                    }
                    else
                    {
                        // ComicRack uses 0 or -1 or empty tags for undefined Year, Month, Day, Count, PageCount
                        if (intVal <= 0) elementsToRemove.Add(elem);
                        else elem.Value = intVal.ToString();
                    }
                }
                else if (localName.Equals("CommunityRating", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(val) || !decimal.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal ratingVal))
                    {
                        elementsToRemove.Add(elem);
                    }
                    else
                    {
                        elem.Value = ratingVal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                else if (localName.Equals("Manga", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(val) || val.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                    {
                        elem.Value = "Unknown";
                    }
                    else if (val.Equals("1", StringComparison.OrdinalIgnoreCase) || val.Equals("true", StringComparison.OrdinalIgnoreCase) || val.Equals("yes", StringComparison.OrdinalIgnoreCase))
                    {
                        elem.Value = "Yes";
                    }
                    else if (val.Equals("0", StringComparison.OrdinalIgnoreCase) || val.Equals("false", StringComparison.OrdinalIgnoreCase) || val.Equals("no", StringComparison.OrdinalIgnoreCase))
                    {
                        elem.Value = "No";
                    }
                    else if (val.IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0 || val.Equals("YesAndRightToLeft", StringComparison.OrdinalIgnoreCase))
                    {
                        elem.Value = "YesAndRightToLeft";
                    }
                    else
                    {
                        elementsToRemove.Add(elem);
                    }
                }
                else if (localName.Equals("BlackAndWhite", StringComparison.OrdinalIgnoreCase))
                {
                    if (val.Equals("1", StringComparison.OrdinalIgnoreCase) || val.Equals("true", StringComparison.OrdinalIgnoreCase))
                        elem.Value = "Yes";
                    else if (val.Equals("0", StringComparison.OrdinalIgnoreCase) || val.Equals("false", StringComparison.OrdinalIgnoreCase))
                        elem.Value = "No";
                    else if (string.IsNullOrEmpty(val))
                        elementsToRemove.Add(elem);
                }
                else if (localName.Equals("Pages", StringComparison.OrdinalIgnoreCase))
                {
                    int pageIdx = 0;
                    foreach (var pageElem in elem.Elements().Where(p => p.Name.LocalName.Equals("Page", StringComparison.OrdinalIgnoreCase)))
                    {
                        // Validate Image attribute
                        var imgAttr = pageElem.Attribute("Image");
                        if (imgAttr == null || !int.TryParse(imgAttr.Value, out _))
                        {
                            pageElem.SetAttributeValue("Image", pageIdx);
                        }

                        // Clean integer attributes
                        foreach (string attrName in new[] { "ImageSize", "ImageWidth", "ImageHeight" })
                        {
                            var attr = pageElem.Attribute(attrName);
                            if (attr != null && (!long.TryParse(attr.Value, out long numVal) || numVal < 0))
                            {
                                attr.Remove();
                            }
                        }

                        // Clean DoublePage boolean attribute
                        var dpAttr = pageElem.Attribute("DoublePage");
                        if (dpAttr != null)
                        {
                            if (dpAttr.Value.Equals("1", StringComparison.OrdinalIgnoreCase) || dpAttr.Value.Equals("true", StringComparison.OrdinalIgnoreCase))
                                dpAttr.Value = "true";
                            else if (dpAttr.Value.Equals("0", StringComparison.OrdinalIgnoreCase) || dpAttr.Value.Equals("false", StringComparison.OrdinalIgnoreCase))
                                dpAttr.Value = "false";
                            else if (!bool.TryParse(dpAttr.Value, out _))
                                dpAttr.Remove();
                        }

                        pageIdx++;
                    }
                }
            }

            foreach (var toRemove in elementsToRemove)
            {
                toRemove.Remove();
            }

            return xdoc.ToString(SaveOptions.DisableFormatting);
        }
        catch
        {
            // If XDocument parsing fails on malformed XML, regex strip empty numeric tags
            string fixedXml = Regex.Replace(cleaned, @"<(Count|Volume|AlternateCount|Year|Month|Day|PageCount)[^>]*>\s*</\1>", "");
            fixedXml = Regex.Replace(fixedXml, @"<(Count|Volume|AlternateCount|Year|Month|Day|PageCount)[^>]*/>", "");
            fixedXml = Regex.Replace(fixedXml, @"<(Manga)[^>]*>\s*(true|1)\s*</\1>", "<Manga>Yes</Manga>", RegexOptions.IgnoreCase);
            fixedXml = Regex.Replace(fixedXml, @"<(Manga)[^>]*>\s*(false|0)\s*</\1>", "<Manga>No</Manga>", RegexOptions.IgnoreCase);
            return fixedXml;
        }
    }

    /// <summary>
    /// Robust regex-based fallback parser for reading XML fields when standard deserialization fails.
    /// </summary>
    public static ComicInfo FallbackParseComicInfoXml(string rawXml)
    {
        var info = new ComicInfo();
        string GetTag(string tagName)
        {
            var match = Regex.Match(rawXml, $@"<{tagName}[^>]*>(.*?)</{tagName}>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }

        int? GetIntTag(string tagName)
        {
            string s = GetTag(tagName);
            return int.TryParse(s, out int val) && val >= 0 ? val : null;
        }

        info.Title = GetTag("Title") is { Length: > 0 } t ? t : null;
        info.Series = GetTag("Series") is { Length: > 0 } s ? s : null;
        info.Number = GetTag("Number") is { Length: > 0 } n ? n : null;
        info.Count = GetIntTag("Count");
        info.Volume = GetIntTag("Volume");
        info.AlternateSeries = GetTag("AlternateSeries") is { Length: > 0 } aser ? aser : null;
        info.AlternateNumber = GetTag("AlternateNumber") is { Length: > 0 } anum ? anum : null;
        info.AlternateCount = GetIntTag("AlternateCount");
        info.Summary = GetTag("Summary") is { Length: > 0 } sum ? sum : null;
        info.Notes = GetTag("Notes") is { Length: > 0 } not ? not : null;
        info.Year = GetIntTag("Year");
        info.Month = GetIntTag("Month");
        info.Day = GetIntTag("Day");
        info.Writer = GetTag("Writer") is { Length: > 0 } w ? w : null;
        info.Penciller = GetTag("Penciller") is { Length: > 0 } pen ? pen : null;
        info.Inker = GetTag("Inker") is { Length: > 0 } ink ? ink : null;
        info.Colorist = GetTag("Colorist") is { Length: > 0 } col ? col : null;
        info.Letterer = GetTag("Letterer") is { Length: > 0 } let ? let : null;
        info.CoverArtist = GetTag("CoverArtist") is { Length: > 0 } cov ? cov : null;
        info.Editor = GetTag("Editor") is { Length: > 0 } ed ? ed : null;
        info.Publisher = GetTag("Publisher") is { Length: > 0 } pub ? pub : null;
        info.Imprint = GetTag("Imprint") is { Length: > 0 } imp ? imp : null;
        info.Genre = GetTag("Genre") is { Length: > 0 } gen ? gen : null;
        info.Tags = GetTag("Tags") is { Length: > 0 } tag ? tag : null;
        info.Web = GetTag("Web") is { Length: > 0 } web ? web : null;
        info.PageCount = GetIntTag("PageCount");
        info.LanguageISO = GetTag("LanguageISO") is { Length: > 0 } lang ? lang : null;
        info.Format = GetTag("Format") is { Length: > 0 } fmt ? fmt : null;
        info.BlackAndWhite = GetTag("BlackAndWhite") is { Length: > 0 } bw ? bw : null;
        info.Characters = GetTag("Characters") is { Length: > 0 } ch ? ch : null;
        info.Teams = GetTag("Teams") is { Length: > 0 } tm ? tm : null;
        info.Locations = GetTag("Locations") is { Length: > 0 } loc ? loc : null;
        info.ScanInformation = GetTag("ScanInformation") is { Length: > 0 } scn ? scn : null;
        info.StoryArc = GetTag("StoryArc") is { Length: > 0 } arc ? arc : null;
        info.SeriesGroup = GetTag("SeriesGroup") is { Length: > 0 } sg ? sg : null;
        info.AgeRating = GetTag("AgeRating") is { Length: > 0 } ar ? ar : null;
        info.MainCharacterOrTeam = GetTag("MainCharacterOrTeam") is { Length: > 0 } mct ? mct : null;
        info.Review = GetTag("Review") is { Length: > 0 } rev ? rev : null;

        string crStr = GetTag("CommunityRating");
        if (decimal.TryParse(crStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal crVal))
            info.CommunityRating = crVal;

        string mangaStr = GetTag("Manga");
        if (mangaStr.Equals("Yes", StringComparison.OrdinalIgnoreCase) || mangaStr.Equals("1") || mangaStr.Equals("true", StringComparison.OrdinalIgnoreCase))
            info.Manga = MangaDirection.Yes;
        else if (mangaStr.Equals("YesAndRightToLeft", StringComparison.OrdinalIgnoreCase) || mangaStr.IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0)
            info.Manga = MangaDirection.YesAndRightToLeft;
        else if (mangaStr.Equals("No", StringComparison.OrdinalIgnoreCase) || mangaStr.Equals("0") || mangaStr.Equals("false", StringComparison.OrdinalIgnoreCase))
            info.Manga = MangaDirection.No;

        return info;
    }

    /// <summary>
    /// Validates an XML file against the official ComicInfo.xsd schema.
    /// </summary>
    public static void ValidateXml(string xmlPath)
    {
        if (!File.Exists(xmlPath))
        {
            return;
        }

        var schemaPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Schema", "ComicInfo.xsd");
        if (!File.Exists(schemaPath))
        {
            AppLogger.LogWarning($"XSD schema not found at '{schemaPath}'. Skipping validation.");
            return;
        }

        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema
        };
        settings.Schemas.Add(null, schemaPath);
        settings.ValidationEventHandler += (sender, args) =>
        {
            if (args.Severity == XmlSeverityType.Error)
            {
                AppLogger.LogWarning($"Schema validation error in {xmlPath}: {args.Message}");
            }
            else
            {
                AppLogger.LogWarning($"Schema validation warning in {xmlPath}: {args.Message}");
            }
        };

        try
        {
            using var reader = XmlReader.Create(xmlPath, settings);
            while (reader.Read()) { }
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"Schema validation exception in {xmlPath}: {ex.Message}");
        }
    }
}
