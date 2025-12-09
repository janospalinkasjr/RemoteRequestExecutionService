using System.Collections.Concurrent;
using RemoteExec.Api.Core.Interfaces;

namespace RemoteExec.Api.Infrastructure.Services
{
    public class InMemoryMetricsCollector : IMetricsCollector
    {
        private readonly ConcurrentDictionary<string, long> _counters = new();
        private readonly ConcurrentDictionary<string, ConcurrentBag<double>> _latencies = new();

        public void RecordRequest(string executorType)
        {
            Increment($"requests_total_{executorType}");
        }

        public void RecordSuccess(string executorType)
        {
            Increment($"requests_success_{executorType}");
        }

        public void RecordFailure(string executorType, bool isTransient)
        {
            Increment($"requests_failed_{executorType}");
            if (isTransient) Increment($"requests_transient_error_{executorType}");
        }

        public void RecordLatency(string executorType, double milliseconds)
        {
            var key = $"latency_{executorType}";
            var bag = _latencies.GetOrAdd(key, _ => new ConcurrentBag<double>());
            bag.Add(milliseconds);
        }

        private void Increment(string key)
        {
            _counters.AddOrUpdate(key, 1, (_, v) => v + 1);
        }

        public object GetSnapshot()
        {
            var result = new Dictionary<string, object>(_counters.ToDictionary(k => k.Key, v => (object)v.Value));
            
            foreach (var kvp in _latencies)
            {
                var values = kvp.Value.OrderBy(x => x).ToList();
                if (values.Count == 0) continue;

                result[$"{kvp.Key}_avg"] = values.Average();
                int p95Index = (int)(values.Count * 0.95);
                result[$"{kvp.Key}_p95"] = values[p95Index];
            }

            return result;
        }
    }
}