namespace Shamus.Core;

/// <summary>Per-tick input: move direction components in {-1,0,1}, fire held.</summary>
public readonly record struct InputState(int MoveX, int MoveY, bool Fire);
