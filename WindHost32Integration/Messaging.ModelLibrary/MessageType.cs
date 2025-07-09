namespace Messaging.ModelLibrary;

public enum MessageType
{
    Text,
    Error,
    Handshake,
    LaunchRequest,
    LaunchResponse,
    StatusRequest,
    StatusResponse,
    HealthCheck,
    HealthCheckResponse,
    ProcessList,
    ProcessListResponse,
    StopProcess,
    StopProcessResponse
}
