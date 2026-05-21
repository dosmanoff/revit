namespace AutoNumbering.Engine;

public class NumberingConfig
{
    public string Prefix { get; set; } = string.Empty;
    public string Suffix { get; set; } = string.Empty;
    public string TargetParameter { get; set; } = "Mark";
    public string SortByParameter { get; set; } = string.Empty;
    public bool SortDescending { get; set; } = false;
    public int StartNumber { get; set; } = 1;
    public int Step { get; set; } = 1;
    public int MinDigits { get; set; } = 1;
}
