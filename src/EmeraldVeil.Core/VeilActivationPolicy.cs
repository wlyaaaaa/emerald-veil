namespace EmeraldVeil.Core;

public enum VeilMode
{
    Hidden,
    Idle,
    Preview,
}

public sealed class VeilActivationPolicy
{
    public VeilActivationPolicy(TimeSpan activationDelay)
    {
        if (activationDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(activationDelay),
                "Activation delay must be positive.");
        }

        ActivationDelay = activationDelay;
    }

    public TimeSpan ActivationDelay { get; }

    public VeilMode Evaluate(
        IdleObservation observation,
        bool isPaused,
        bool previewRequested)
    {
        if (previewRequested)
        {
            return VeilMode.Preview;
        }

        if (isPaused || !observation.IsReliable)
        {
            return VeilMode.Hidden;
        }

        return observation.IdleDuration >= ActivationDelay
            ? VeilMode.Idle
            : VeilMode.Hidden;
    }
}
