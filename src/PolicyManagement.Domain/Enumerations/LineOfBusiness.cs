namespace PolicyManagement.Domain.Enumerations;

/// <summary>
/// Represents the line of business for an insurance policy.
/// Stored in the database as a string using a value converter:
///   AccidentAndHealth ↔ "A&amp;H"
///   All other values use their enum name verbatim.
/// </summary>
public enum LineOfBusiness
{
    Property,
    Casualty,
    /// <summary>Accident &amp; Health — stored as "A&amp;H" in the database.</summary>
    AccidentAndHealth,
    Marine
}
