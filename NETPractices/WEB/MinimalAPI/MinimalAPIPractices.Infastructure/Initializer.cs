
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MinimalAPIPractices.Domain;
using MinimalAPIPractices.Domain.Service;
using MinimalAPIPractices.Infastructure.Context;
using MinimalAPIPractices.Infastructure.Services;

namespace MinimalAPIPractices.Infastructure
{
    public static class Initializer
    {
        public static void AddInfastructure(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddDbContext<ApplicationContext>(options =>
            {
                options.UseSqlite("Data Source=movieratings.db");
            });
            serviceCollection.AddTransient<IService<User>, BaseService<User>>();
            serviceCollection.AddTransient<IService<Movie>, BaseService<Movie>>();
            serviceCollection.AddTransient<IService<Rating>, BaseService<Rating>>();
        }
    }
}
