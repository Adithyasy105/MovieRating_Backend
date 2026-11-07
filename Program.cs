using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using MongoDB.Driver;
using MovieRating.Aggregation;
using MovieRating.BackgroundJobs.DeferredReviewAggregation;
using MovieRating.BackgroundJobs.MoviePurge;
using MovieRating.Configuration;
using MovieRating.Data;
using MovieRating.Exceptions;
using MovieRating.Models;
using MovieRating.Repositories;
using MovieRating.Services;
using MovieRating.Services.Movies;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails(); // This ensures full framework compatibility with RFC 7807 error formatting standards.

builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Register API controllers.
builder.Services.AddControllers();

// Register Swagger document generation and UI support.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MovieRating API",
        Version = "v1"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter a valid JWT token."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

builder.Services
    .AddOptions<DeferredReviewAggregationOptions>()
    .BindConfiguration(DeferredReviewAggregationOptions.SectionName)
    .Validate(
        options => options.BatchSize is >= 1 and <= 500,
        "Batch size must be between 1 and 500.")
    .Validate(
        options => options.PollIntervalSeconds > 0,
        "Poll interval must be greater than zero.")
    .Validate(
        options => options.LeaseDurationSeconds > 0,
        "Lease duration must be greater than zero.")
    .ValidateOnStart();

builder.Services
    .AddOptions<MoviePurgeOptions>()
    .BindConfiguration("MoviePurge")
    .Validate(
        options => options.BatchSize is >= 1 and <= 500,
        "Batch size must be between 1 and 500.")
    .Validate(
        options => options.PollIntervalSeconds > 0,
        "Poll interval must be greater than zero.")
    .Validate(
        options => options.LeaseDurationSeconds > 0,
        "Lease duration must be greater than zero.")
    .ValidateOnStart();

// Bind and validate MongoDB configuration at startup.
builder.Services
    .AddOptions<MongoDbOptions>()
    .BindConfiguration(MongoDbOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(
        options => IsValidMongoConnectionString(
            options.ConnectionString),"MongoDB connection string is invalid.")
    .ValidateOnStart();

// Register one MongoDB client for the application lifetime.
builder.Services.AddSingleton<IMongoClient>(serviceProvider =>
{
    var options = serviceProvider
        .GetRequiredService<IOptions<MongoDbOptions>>().Value;

    return new MongoClient(options.ConnectionString);
});

// Register the selected database using the shared client.
builder.Services.AddSingleton<IMongoDatabase>(serviceProvider =>
{
    var options = serviceProvider
        .GetRequiredService<IOptions<MongoDbOptions>>().Value;

    var client = serviceProvider
        .GetRequiredService<IMongoClient>();

    return client.GetDatabase(options.DatabaseName);
});

// Bind and validate JWT configuration at startup.
builder.Services
    .AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Issuer),
        "JWT issuer must not be empty.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Audience),
        "JWT audience must not be empty.")
    .Validate(
        options => IsValidSigningKey(options.SigningKey),
        "JWT signing key must be valid Base64 and decode to at least 32 bytes.")
    .ValidateOnStart();

// Register JWT bearer authentication. schema among cookies session and bearer
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

// Configure validation rules for incoming JWTs and AddOptions<JwtBearerOptions>("Bearer") configures the named options instance associated with the bearer scheme.
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptionsAccessor) =>
        {
            var jwt = jwtOptionsAccessor.Value;
            var signingKeyBytes = Convert.FromBase64String(jwt.SigningKey);

            bearerOptions.MapInboundClaims = false; // original claim, this completely turn off this automatic translation. and preser orginal sub exp and role email and all insted of xml mapping

            bearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,

                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes),

                    ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },

                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,

                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,

                    ValidateLifetime = true,
                    //ClockSkew = TimeSpan.FromSeconds(30),


                    NameClaimType = "name",
                    RoleClaimType = "role"
                };
        });

// Register authorization policies.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        "AuthenticatedUser",
        policy => policy.RequireAuthenticatedUser());

    options.AddPolicy(
        "AdminOnly",
        policy => policy
            .RequireAuthenticatedUser()
            .RequireRole("Admin"));
});

// Register hosted services and repositories.

builder.Services.AddScoped<IReviewAggregationStrategy, ImmediateReviewAggregationStrategy>();

//builder.Services.AddScoped<IReviewAggregationStrategy, DeferredReviewAggregationStrategy>();

builder.Services.AddScoped<DeferredReviewProcessor>();
builder.Services.AddScoped<MoviePurgeProcessor>();



builder.Services.AddHostedService<DeferredReviewAggregationWorker>();
builder.Services.AddHostedService<MoviePurgeWorker>();

//movies 
builder.Services.AddHostedService<MongoIndexInitializer>();
builder.Services.AddScoped<IMovieRepository, MongoMovieRepository>();
builder.Services.AddScoped<IMovieService, MovieService>();


//users
builder.Services.AddScoped<IUserRepository, MongoUserRepository>();
builder.Services.AddScoped<IUserService, UserService>();

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();


//review rating
builder.Services.AddScoped<IMovieRatingRepository,MongoMovieRatingRepository>();
builder.Services.AddScoped<IReviewRepository, MongoReviewRepository>();
builder.Services.AddScoped<IReviewService, ReviewService>();

builder.Services.AddScoped<IBackgroundJobRepository,MongoBackgroundJobRepository>();




var app = builder.Build();

// Expose Swagger documentation in Development.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "MovieRating API v1");
    });
}
app.UseExceptionHandler();

app.UseHttpsRedirection();

// Authentication must run before authorization.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

static bool IsValidMongoConnectionString(string connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return false;
    }

    try
    {
        _ = MongoUrl.Create(connectionString);
        return true;
    }
    catch (Exception)
    {
        return false;
    }
}

static bool IsValidSigningKey(string signingKey)
{
    if (string.IsNullOrWhiteSpace(signingKey))
    {
        return false;
    }

    try
    {
        var keyBytes = Convert.FromBase64String(signingKey);

        return keyBytes.Length >= 32;
    }
    catch (FormatException)
    {
        return false;
    }
}
