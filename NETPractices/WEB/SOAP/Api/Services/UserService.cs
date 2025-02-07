using ModelLibrary;
using ModelLibrary.Models;

namespace Api.Services
{
    public class UserService : IUserService
    {
        private readonly List<User> _users;
        public UserService()
        {
            _users = new List<User>
            {
                new User { Id = 1, Name = "John Doe", Email = "}" },
                new User { Id = 2, Name = "Jane Doe", Email = "}" },
                new User { Id = 3, Name = "John Smith", Email = "}" },
                new User { Id = 4, Name = "Jane Smith", Email = "}"}
            };
        }

        public List<User> GetUsers()
        {
            return _users;
        }

        public User GetUser(int id)
        {
            return _users.FirstOrDefault(u => u.Id == id)!;
        }

        public void AddUser(User user)
        {
            _users.Add(user);
        }

        public void UpdateUser(User user)
        {
        }

        public bool DeleteUser(int id)
        {
            var user = _users.FirstOrDefault(u => u.Id == id);
            if (user == null)
            {
                return false;
            }
            _users.Remove(user);
            return true;
        }
    }
}
