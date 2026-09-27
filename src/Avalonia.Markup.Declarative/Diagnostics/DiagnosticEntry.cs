using System;

namespace Avalonia.Markup.Declarative.Diagnostics;

/// <summary>
/// Severity of a recorded <see cref="DiagnosticEntry"/>.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>Informational diagnostic.</summary>
    Info,
    /// <summary>Diagnostic describing a recoverable issue.</summary>
    Warning,
    /// <summary>Diagnostic describing an error.</summary>
    Error
}

/// <summary>
/// Logical source category of a recorded <see cref="DiagnosticEntry"/>.
/// </summary>
public enum DiagnosticCategory
{
    /// <summary>UI build errors (e.g. <see cref="ViewBuildingException"/>).</summary>
    Build,

    /// <summary>Avalonia data-binding errors (captured via the logging sink).</summary>
    Binding,

    /// <summary>Automatic binding conversion failures.</summary>
    Converter,

    /// <summary>Unhandled exceptions from the UI dispatcher or unobserved tasks (e.g. event handlers).</summary>
    Runtime,

    /// <summary>Anything else.</summary>
    Other
}

/// <summary>
/// A single immutable diagnostics record kept by <see cref="DiagnosticsErrorLog"/>.
/// </summary>
/// <param name="Timestamp">When the entry was recorded.</param>
/// <param name="Severity">The severity of the entry.</param>
/// <param name="Category">The logical source category.</param>
/// <param name="Source">A short description of where the problem originated (control type/path).</param>
/// <param name="Message">The human-readable message. For build errors this already carries file/line.</param>
public sealed record DiagnosticEntry(
    DateTimeOffset Timestamp,
    DiagnosticSeverity Severity,
    DiagnosticCategory Category,
    string Source,
    string Message)
{
    /// <summary>
    /// Renders the entry as a single agent-friendly line.
    /// </summary>
    public override string ToString() =>
        $"[{Timestamp:HH:mm:ss.fff}] {Severity}/{Category} {Source}: {Message}";
}
