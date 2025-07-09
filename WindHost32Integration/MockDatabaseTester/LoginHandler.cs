
using System.Net.Http.Json;
using System.Net.Mime;
using Iotech.Link.Libs.Modules.LinkRestAPI.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Serilog;

namespace MockDatabaseTester
{
    internal class LoginHandler
    {
        private HttpClient Client
        {
            get
            {
                var client = new HttpClient(new HttpClientHandler(){ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator});
                client.BaseAddress = new Uri("https://192.168.3.71:5001");
                return client;
            }
        }
        public LoginHandler()
        {
        }

        Guid GetDeviceId()
        {
            try
            {
                string registryPath = @"SOFTWARE\Microsoft\SQMClient";
                string registryKey = "MachineId";

                using RegistryKey key = Registry.LocalMachine.OpenSubKey(registryPath);
                if (key != null)
                {
                    object machineIdValue = key.GetValue(registryKey);
                    if (machineIdValue != null)
                    {
                        return Guid.Parse(machineIdValue.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while retrieving the SQMClient Machine ID: {ex.Message}");
            }

            // Return an empty Guid if the key is not found
            return Guid.Empty;
        }
        internal async Task<bool> LoginAsync(string username, string password)
        {
            var endPoint = $"/api/Auth/Login?deviceId={GetDeviceId()}";
            try
            {
                var loginDto = new UserLoginDto() { UserName = username, Password = password };
                var response = await Client.PostAsJsonAsync($"{endPoint}",loginDto );
                if (response.IsSuccessStatusCode)
                {
                    var loginResponse = await response.Content.ReadFromJsonAsync<LinkApiResponseDto<LoginResponseDto>>();
                    if (loginResponse != null)
                    {
                        OnLoginSuccess?.Invoke(loginResponse.Value);
                        return true;
                    }
                }
                else
                {
                    Console.WriteLine($"Login failed: {response.StatusCode}");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"An error occurred while creating the HTTP client: {e.Message}");
            }
            return false;
        }

        public Action<LoginResponseDto?> OnLoginSuccess { get; set; } = null!;
    }
}
