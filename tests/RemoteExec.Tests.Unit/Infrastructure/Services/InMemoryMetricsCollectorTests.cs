using RemoteExec.Api.Infrastructure.Services;

namespace RemoteExec.Tests.Unit.Infrastructure.Services
{
    public class InMemoryMetricsCollectorTests
    {
        private readonly InMemoryMetricsCollector _collector;

        public InMemoryMetricsCollectorTests()
        {
            _collector = new InMemoryMetricsCollector();
        }

        [Fact]
        public void RecordRequest_IncrementsTotalCounter()
        {
            // Arrange
            var executorType = "http";

            // Act
            _collector.RecordRequest(executorType);

            // Assert
            var snapshot = (Dictionary<string, object>)_collector.GetSnapshot();
            Assert.True(snapshot.ContainsKey("requests_total_http"));
            Assert.Equal(1L, snapshot["requests_total_http"]);
        }

        [Fact]
        public void RecordSuccess_IncrementsSuccessCounter()
        {
            // Arrange
            var executorType = "powershell";

            // Act
            _collector.RecordSuccess(executorType);

            // Assert
            var snapshot = (Dictionary<string, object>)_collector.GetSnapshot();
            Assert.True(snapshot.ContainsKey("requests_success_powershell"));
            Assert.Equal(1L, snapshot["requests_success_powershell"]);
        }

        [Fact]
        public void RecordFailure_IncrementsFailureCounter_AndTransientIfTrue()
        {
            // Arrange
            var executorType = "http";

            // Act
            _collector.RecordFailure(executorType, isTransient: true);

            // Assert
            var snapshot = (Dictionary<string, object>)_collector.GetSnapshot();
            Assert.Equal(1L, snapshot["requests_failed_http"]);
            Assert.Equal(1L, snapshot["requests_transient_error_http"]);
        }

        [Fact]
        public void RecordFailure_IncrementsFailureCounter_Only_IfTransientIsFalse()
        {
            // Arrange
            var executorType = "http";

            // Act
            _collector.RecordFailure(executorType, isTransient: false);

            // Assert
            var snapshot = (Dictionary<string, object>)_collector.GetSnapshot();
            Assert.Equal(1L, snapshot["requests_failed_http"]);
            Assert.False(snapshot.ContainsKey("requests_transient_error_http"));
        }

        [Fact]
        public void RecordLatency_CalculatesStatsCorrectly()
        {
            // Arrange
            var executorType = "http";
            
            // Act
            _collector.RecordLatency(executorType, 100);
            _collector.RecordLatency(executorType, 200);
            _collector.RecordLatency(executorType, 300);

            // Assert
            var snapshot = (Dictionary<string, object>)_collector.GetSnapshot();
            
            // Avg = (100+200+300)/3 = 200
            Assert.Equal(200d, (double)snapshot["latency_http_avg"]);

            // P95: index = 0.95 * 3 = 2.85 -> 2. So values[2] -> 300 (sorted: 100, 200, 300)
            Assert.Equal(300d, (double)snapshot["latency_http_p95"]);
        }
        
        [Fact]
        public void GetSnapshot_ReturnsEmpty_WhenNoMetrics()
        {
            // Act
            var snapshot = (Dictionary<string, object>)_collector.GetSnapshot();

            // Assert
            Assert.Empty(snapshot);
        }
    }
}