namespace RemoteExec.Api.Core.Interfaces
{
    public interface IMetricsCollector
    {
        void RecordRequest(string executorType);
        void RecordSuccess(string executorType);
        void RecordFailure(string executorType, bool isTransient);
        void RecordLatency(string executorType, double milliseconds);
        object GetSnapshot();
    }
}