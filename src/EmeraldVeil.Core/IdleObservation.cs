namespace EmeraldVeil.Core;

public readonly record struct IdleObservation(bool IsReliable, TimeSpan IdleDuration);
