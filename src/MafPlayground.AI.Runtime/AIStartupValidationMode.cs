namespace MafPlayground.AI;

/// <summary>LocalInspection defers model/provider readiness to availability and execution. Structural DI/options validation remains enabled.</summary>
public enum AIStartupValidationMode { Strict, LocalInspection }
internal sealed record AIStartupValidationOptions(AIStartupValidationMode Mode);
