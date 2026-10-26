using MafPlayground.Studio.Contracts;

namespace MafPlayground.Studio.Execution;

public interface IStudioEventSink
{
    void Record(StudioEvent studioEvent);
}
