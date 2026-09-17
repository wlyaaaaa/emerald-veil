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
        bool previewRequested,
        bool externalProtectionActive = false)
    {
        if (externalProtectionActive || isPaused || !observation.IsReliable)
        {
            return VeilMode.Hidden;
        }

        if (previewRequested)
        {
            return VeilMode.Preview;
        }

        return observation.IdleDuration >= ActivationDelay
            ? VeilMode.Idle
            : VeilMode.Hidden;
    }
}
