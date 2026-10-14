namespace ReportingTool.Api.Interfaces.Greetings;

/// <summary>Reads and writes the hello-world slice's greetings (see <see cref="Greeting"/>).</summary>
public interface IGreetingService
{
    /// <summary>The newest <paramref name="count"/> greetings, newest first.</summary>
    Task<IReadOnlyList<GreetingResponse>> ListLatestAsync(int count, CancellationToken ct = default);

    /// <summary>
    /// Saves a greeting stamped with the current UTC time. Takes no cancellation token on
    /// purpose: once a valid greeting is accepted it is saved even if the caller disconnects.
    /// </summary>
    Task<GreetingResponse> CreateAsync(string message);
}
