using Microsoft.EntityFrameworkCore;
using MinimalAPIPractices.Domain;
using MinimalAPIPractices.Domain.Service;
using MinimalAPIPractices.Infastructure.Context;
using MinimalAPIPractices.Models.Abstract;

namespace MinimalAPIPractices.Models.Implementation
{
    public class UserEndpoint : IEndpoint
    {
        public void RegisterEndpoints(IEndpointRouteBuilder endpointRouteBuilder)
        {
            var userGroup = endpointRouteBuilder.MapGroup("/users").WithTags("user");

            // Get all users
            userGroup.MapGet("", async (IService<User> userService) =>
                await userService.GetAll());

            // Get user by ID
            userGroup.MapGet("{id:guid}", async (Guid id, IService<User> userService) =>
            {
                var user = await userService.GetById(id);
                return user == null ? Results.NotFound() : Results.Ok(user);
            });

            // Register new user - Note: keeping this as /register instead of moving it under /users
            userGroup.MapPost("/register", async (IService<User> userService, User user) =>
            {
                // Validate the user
                if (string.IsNullOrEmpty(user.UserName) || string.IsNullOrEmpty(user.Email) || string.IsNullOrEmpty(user.Password))
                {
                    return Results.BadRequest("Username, email, and password are required");
                }

                // Create the user
                var createdUser = await userService.Create(user);

                if (createdUser == null)
                {
                    return Results.BadRequest("Failed to create user");
                }

                // Don't return the password in the response
                var returnUser = new User
                {
                    Id = createdUser.Id,
                    UserName = createdUser.UserName,
                    Email = createdUser.Email,
                    FirstName = createdUser.FirstName,
                    LastName = createdUser.LastName
                };

                return Results.Created($"/users/{createdUser.Id}", returnUser);
            });
        }
    }

    public class MovieEndpoint : IEndpoint
    {
        public void RegisterEndpoints(IEndpointRouteBuilder endpointRouteBuilder)
        {
            var movieGroup = endpointRouteBuilder.MapGroup("/movies").WithTags("Movie");

            // Get all movies
            movieGroup.MapGet("", async (IService<Movie> movieService) =>
                await movieService.GetAll());

            // Get movie by ID
            movieGroup.MapGet("{id:guid}", async (Guid id, IService<Movie> movieService) =>
            {
                var movie = await movieService.GetById(id);
                return movie == null ? Results.NotFound() : Results.Ok(movie);
            });

            // Create a new movie
            movieGroup.MapPost("", async (IService<Movie> movieService, Movie movie) =>
            {
                // Validate the movie
                if (string.IsNullOrEmpty(movie.Title) || movie.ReleaseYear <= 0)
                {
                    return Results.BadRequest("Title and release year are required");
                }

                var createdMovie = await movieService.Create(movie);

                if (createdMovie == null)
                {
                    return Results.BadRequest("Failed to create movie");
                }

                return Results.Created($"/movies/{createdMovie.Id}", createdMovie);
            });

            // Update an existing movie
            movieGroup.MapPut("{id:guid}", async (Guid id, IService<Movie> movieService, Movie movie) =>
            {
                var updatedMovie = await movieService.Update(id, movie);

                if (updatedMovie == null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(updatedMovie);
            });

            // Delete a movie
            movieGroup.MapDelete("{id:guid}", async (Guid id, IService<Movie> movieService) =>
            {
                var deletedMovie = await movieService.Delete(id);

                if (deletedMovie == null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(deletedMovie);
            });

            // Get ratings for a specific movie
            movieGroup.MapGet("{movieId:guid}/ratings", async (Guid movieId, ApplicationContext context) =>
            {
                var ratings = await context.Ratings
                    .Where(r => r.Movie.Id == movieId)
                    .ToListAsync();

                return Results.Ok(ratings);
            });
        }
    }

    public class RatingEndpoint : IEndpoint
    {
        public void RegisterEndpoints(IEndpointRouteBuilder endpointRouteBuilder)
        {
            var ratingGroup = endpointRouteBuilder.MapGroup("/ratings").WithTags("Ratings");

            // Get all ratings
            ratingGroup.MapGet("", async (IService<Rating> ratingService) =>
                await ratingService.GetAll());

            // Get rating by ID
            ratingGroup.MapGet("{id:guid}", async (Guid id, IService<Rating> ratingService) =>
            {
                var rating = await ratingService.GetById(id);
                return rating == null ? Results.NotFound() : Results.Ok(rating);
            });

            // Create a new rating
            ratingGroup.MapPost("", async (IService<Rating> ratingService, Rating rating) =>
            {
                // Validate the rating
                if (rating.RatingValue is < 1 or > 5)
                {
                    return Results.BadRequest("Rating value must be between 1 and 5");
                }

                var createdRating = await ratingService.Create(rating);

                if (createdRating == null)
                {
                    return Results.BadRequest("Failed to create rating");
                }

                return Results.Created($"/ratings/{createdRating.Id}", createdRating);
            });
        }
    }
}
