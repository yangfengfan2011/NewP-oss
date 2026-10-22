namespace ICP.Platform.Persistence.Extensions;

public static class PersistenceOptionValidation
{
    public static void Validate(PersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Adapter is null)
        {
            throw new InvalidOperationException("Persistence Adapter must be configured.");
        }
    }
}
