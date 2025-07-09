namespace Models
{
    namespace SharedLibrary.Models
    {
        [Serializable]
        public class ProcessingRequest
        {
            public string Id { get; set; } = Guid.NewGuid().ToString();
            public string Operation { get; set; }
            public Dictionary<string, object> Parameters { get; set; } = new();
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }

        [Serializable]
        public class ProcessingResponse
        {
            public string RequestId { get; set; }
            public bool Success { get; set; }
            public string Message { get; set; }
            public object Data { get; set; }
            public int Progress { get; set; }
            public DateTime CompletedAt { get; set; } = DateTime.Now;
        }

        [Serializable]
        public class DataRecord
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public decimal Value { get; set; }
            public DateTime CreatedAt { get; set; }
        }
    }
}
