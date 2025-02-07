using System.ServiceModel;
using ModelLibrary.Models;

namespace ModelLibrary
{
    [ServiceContract]
    public interface ICalculatorService
    {
        [OperationContract]
        int Add(int a, int b);

        [OperationContract]
        int Subtract(int a, int b);

        [OperationContract]
        int Multiply(int a, int b);

        [OperationContract]
        double Divide(int a, int b);
    }

    [ServiceContract] // ✅ Ensure namespace is explicitly defined
    public interface IUserService
    {
        [OperationContract]
        List<User> GetUsers();

        [OperationContract]
        User GetUser(int id);

        [OperationContract]
        void AddUser(User user);

        [OperationContract]
        void UpdateUser(User user);

        [OperationContract]
        bool DeleteUser(int id);
    }
}
