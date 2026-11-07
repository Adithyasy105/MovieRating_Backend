
using MongoDB.Bson;
using MongoDB.Driver;
using MovieRating.Models;

namespace MovieRating.Repositories;

public sealed class MongoUserRepository : IUserRepository
{
    private readonly IMongoCollection<User> _users;

    public MongoUserRepository(IMongoDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        _users = database.GetCollection<User>("users");
    }

    public async Task<User?> GetByNormalizedEmailAsync(string normalizedEmail,CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);

        var filter = Builders<User>.Filter.Eq(
            user => user.NormalizedEmail,
            normalizedEmail);

        return await _users
            .Find(filter)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<UserCreateResult> CreateAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrWhiteSpace(user.NormalizedEmail))
        {
            throw new ArgumentException("A normalized email is required.",nameof(user));
        }

        // Generate the MongoDB ObjectId before insertion.
        user.Id ??= ObjectId.GenerateNewId().ToString(); //null-coalescing assignment operator checks left exisst is nor then only rught expression

        try
        {
            await _users.InsertOneAsync(
                user,
                cancellationToken: cancellationToken);

            return UserCreateResult.Created;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // The unique email index prevents duplicate accounts,
                // including simultaneous registration requests.
                return UserCreateResult.EmailAlreadyExists;
            }
    }

    public async Task<bool> UpdatePasswordHashAsync(string userId,string passwordHash,
    CancellationToken cancellationToken = default)
    {
        if (!ObjectId.TryParse(userId, out _))
        {
            return false;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        var filter = Builders<User>.Filter.Eq(
            user => user.Id,
            userId);

        var update = Builders<User>.Update.Set(
            user => user.PasswordHash,
            passwordHash);

        var result = await _users.UpdateOneAsync(
            filter,
            update,
            cancellationToken: cancellationToken);

        return result.MatchedCount > 0; // Return true if a user was found and updated; false otherwise.
    }

    public async Task<User?> GetByIdAsync(string userId,CancellationToken cancellationToken = default)
    {
        if (!ObjectId.TryParse(userId, out _))
        {
            return null;
        }

        var filter = Builders<User>.Filter.Eq(user => user.Id,userId);

        return await _users.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<User>> GetAllAsync(
    int skip,
    int take,
    CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);

        return await _users
            .Find(Builders<User>.Filter.Empty)
            .SortByDescending(user => user.CreatedAtUtc)
            .ThenBy(user => user.Id)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<long> CountAllAsync(
    CancellationToken cancellationToken = default)
    {
        return await _users.CountDocumentsAsync(Builders<User>.Filter.Empty,cancellationToken: cancellationToken);
    }

    public async Task<UserStatusUpdateResult> UpdateStatusAsync(
    string userId,
    string status,
    long expectedVersion,
    CancellationToken cancellationToken = default)
    {
        if (!ObjectId.TryParse(userId, out _))
            return UserStatusUpdateResult.NotFound;

        var filter = Builders<User>.Filter.And(
            Builders<User>.Filter.Eq(user => user.Id, userId),
            Builders<User>.Filter.Eq(user => user.Version, expectedVersion));

        var update = Builders<User>.Update
            .Set(user => user.Status, status)
            .Set(user => user.UpdatedAtUtc, DateTime.UtcNow)
            .Inc(user => user.Version, 1);

        var result = await _users.UpdateOneAsync(
            filter,
            update,
            cancellationToken: cancellationToken);

        if (result.MatchedCount > 0)
            return UserStatusUpdateResult.Updated;

        var userExists = await GetByIdAsync(userId, cancellationToken);

        return userExists is null
            ? UserStatusUpdateResult.NotFound
            : UserStatusUpdateResult.VersionConflict;
    }
}
