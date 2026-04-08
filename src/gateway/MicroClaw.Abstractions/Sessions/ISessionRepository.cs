namespace MicroClaw.Abstractions.Sessions;

/// <summary>
/// Complete repository contract for persisted sessions and their messages.
/// </summary>
public interface ISessionRepository
{
    IMicroSession? Get(string id);

    IReadOnlyList<IMicroSession> GetAll();

    void Save(IMicroSession microSession);

    bool Delete(string id);

    void AddMessage(string sessionId, SessionMessage message);

    IReadOnlyList<SessionMessage> GetMessages(string sessionId);

    (IReadOnlyList<SessionMessage> Messages, int Total) GetMessagesPaged(
        string sessionId, int skip, int limit);

    void RemoveMessages(string sessionId, IReadOnlySet<string> messageIds);
}
