using System;

namespace InkTag.Core.Exceptions;

/// <summary>
/// Base exception for all InkTag domain-specific operational errors.
/// </summary>
public class InkTagException : Exception
{
    public InkTagException(string message) : base(message) { }
    public InkTagException(string message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when an archive operation fails or encounters an unsupported archive state.
/// </summary>
public class ComicArchiveException : InkTagException
{
    public string? FilePath { get; }

    public ComicArchiveException(string message, string? filePath = null, Exception? innerException = null)
        : base(message, innerException)
    {
        FilePath = filePath;
    }
}

/// <summary>
/// Thrown when an archive file is corrupted, malformed, empty, or fails integrity validation.
/// </summary>
public class ComicArchiveCorruptException : ComicArchiveException
{
    public ComicArchiveCorruptException(string message, string? filePath = null, Exception? innerException = null)
        : base(message, filePath, innerException) { }
}

/// <summary>
/// Thrown when ComicInfo XML fails schema validation, sanitization, or parsing.
/// </summary>
public class MetadataXmlSanitizationException : InkTagException
{
    public string? XmlContentSnippet { get; }

    public MetadataXmlSanitizationException(string message, string? xmlContentSnippet = null, Exception? innerException = null)
        : base(message, innerException)
    {
        XmlContentSnippet = xmlContentSnippet;
    }
}

/// <summary>
/// Thrown when a zip-slip path traversal or unsafe archive entry is detected during extraction.
/// </summary>
public class UnsafeArchiveEntryException : ComicArchiveException
{
    public string? EntryName { get; }

    public UnsafeArchiveEntryException(string message, string entryName, string? filePath = null, Exception? innerException = null)
        : base(message, filePath, innerException)
    {
        EntryName = entryName;
    }
}
