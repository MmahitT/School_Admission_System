using System.Text.RegularExpressions;

namespace SchoolAdmission.Domain;

/// <summary>Student identity rules: a birth registration number is exactly 17 ASCII digits.</summary>
public static partial class StudentId
{
    public const int Length = 17;

    [GeneratedRegex("^[0-9]{17}$")]
    private static partial Regex Pattern();

    public static bool IsValid(string? value) => value is not null && Pattern().IsMatch(value);
}
