using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Iotech.Link.Libs.Modules.LinkRestAPI.DTOs;
using Iotech.Link.Libs.Modules.LinkRestAPI.Models;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace MockDatabaseTester
{
    internal class DatabaseService:BackgroundService
    {
        private readonly LoginHandler _loginHandler;

        public DatabaseService(LoginHandler loginHandler)
        {
            _loginHandler = loginHandler;
            _loginHandler.OnLoginSuccess += LoginHandlerOnLoginSuccess;
        }

        private void LoginHandlerOnLoginSuccess(LoginResponseDto? response)
        {
            var comp = response?.UserCompsWithAuths.FirstOrDefault();
            if (comp is {Comp:{Tags:{}}})
            {
                var tag = comp.Comp.Tags.FirstOrDefault();
                MockDatabase(tag);
            }
        }

        private void MockDatabase(Tag? tag)
        {
            if(tag is null)return;
            string connectionString = $"Host=192.168.3.71;Port=5432;Database={tag.Location};Username=TESAUser;Password=123456";

            // Create and open connection
            using (var connection = new NpgsqlConnection(connectionString))
            {
                connection.Open();

                // Execute a simple query
                using (var cmd = new NpgsqlCommand($"SELECT * FROM LOGSch.{tag.Name}", connection))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            // Process each row
                            string value = reader["column_name"].ToString();
                            Console.WriteLine(value);
                        }
                    }
                }
            }
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var loginTask = _loginHandler.LoginAsync("operator", "Iotech+2015");
            loginTask.ContinueWith(task => { Console.WriteLine(task.Result ? "Login successful" : "Login failed"); }, stoppingToken);
            return Task.CompletedTask;
        }
    }
}
