namespace BPP.Core;

/// <param name="Item">Progress (0..1) of the current item — the image being pulled; null when not reported.</param>
public record BppEvent(string Step, double? Progress, string Message, double? Item = null);
