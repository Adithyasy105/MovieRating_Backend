using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MovieRating.Models;
using MovieRating.Pagination;
namespace MovieRating.Repositories;

public sealed class MongoReviewRepository : IReviewRepository
{
    private readonly IMongoCollection<Review> _reviews;

    public MongoReviewRepository(IMongoDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        _reviews = database.GetCollection<Review>("reviews");
    }

    public async Task<ReviewPage> GetByMoviesAsync(string movieId,ReviewCursor? cursor,int pageSize,
    CancellationToken cancellationToken = default)
    {
        ValidateObjectId(movieId, nameof(movieId));

        if (pageSize < 1 || pageSize > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize),"Page size must be between 1 and 100.");
        }

        if (cursor is not null)
        {
            if (cursor.CreatedAtUtc.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("The cursor timestamp must be UTC.",nameof(cursor));
            }

            ValidateObjectId(cursor.Id, nameof(cursor));
        }

        var filterBuilder = Builders<Review>.Filter;

        var filters = new List<FilterDefinition<Review>>
        {
            filterBuilder.Eq(review => review.MovieId, movieId),
            filterBuilder.Eq(review => review.Status, "Active")
        };

        if (cursor is not null)
        {
            var cursorFilter = filterBuilder.Or(filterBuilder.Lt(review => review.CreatedAtUtc,cursor.CreatedAtUtc),

                filterBuilder.And(filterBuilder.Eq(review => review.CreatedAtUtc,cursor.CreatedAtUtc),

                filterBuilder.Lt(review => review.Id,cursor.Id))
            );
            filters.Add(cursorFilter);
        }

        var filter = filterBuilder.And(filters);

        var reviews = await _reviews
            .Find(filter)
            .SortByDescending(review => review.CreatedAtUtc)
            .ThenByDescending(review => review.Id)
            .Limit(pageSize + 1) //plus one is to identify the hasmore
            .ToListAsync(cancellationToken);

        var hasMore = reviews.Count > pageSize;

        if (hasMore)
        {
            reviews.RemoveAt(pageSize);
        }
        return new ReviewPage(reviews, hasMore);
    }

    public Task<long> CountByMovieAsync(string movieId,CancellationToken cancellationToken = default)
    {
        ValidateObjectId(movieId, nameof(movieId));

        var filter = Builders<Review>.Filter.And(
            Builders<Review>.Filter.Eq(review => review.MovieId, movieId),
            Builders<Review>.Filter.Eq(review => review.Status, "Active"));

        return _reviews.CountDocumentsAsync(filter,cancellationToken: cancellationToken);
    }

    public Task<Review?> GetByIdAsync(
        IClientSessionHandle session,
        string movieId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        ValidateObjectId(movieId, nameof(movieId));
        ValidateObjectId(reviewId, nameof(reviewId));

        var filter = Builders<Review>.Filter.And(
            Builders<Review>.Filter.Eq(review => review.Id, reviewId),
            Builders<Review>.Filter.Eq(review => review.MovieId, movieId),
            Builders<Review>.Filter.Eq(review => review.Status, "Active"));

        return _reviews.Find(session, filter).FirstOrDefaultAsync(cancellationToken);
    }


    //overload of GetByIdAsync without session for deferred mode
    public Task<Review?> GetByIdAsync(
    string movieId,
    string reviewId,
    CancellationToken cancellationToken = default)
    {
        ValidateObjectId(movieId, nameof(movieId));
        ValidateObjectId(reviewId, nameof(reviewId));

        var filter = Builders<Review>.Filter.And(
            Builders<Review>.Filter.Eq(review => review.Id, reviewId),
            Builders<Review>.Filter.Eq(review => review.MovieId, movieId),
            Builders<Review>.Filter.Eq(review => review.Status, "Active"));

        return _reviews.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    //overload of GetByMovieAndUserAsync without session for deferred mode
    public Task<Review?> GetByMovieAndUserAsync(
    string movieId,
    string userId,
    CancellationToken cancellationToken = default)
    {
        ValidateObjectId(movieId, nameof(movieId));
        ValidateObjectId(userId, nameof(userId));

        var filter = Builders<Review>.Filter.And(
            Builders<Review>.Filter.Eq(review => review.MovieId, movieId),
            Builders<Review>.Filter.Eq(review => review.UserId, userId),
            Builders<Review>.Filter.Eq(review => review.Status, "Active"));

        return _reviews.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public Task<Review?> GetByMovieAndUserAsync(
        IClientSessionHandle session,
        string movieId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        ValidateObjectId(movieId, nameof(movieId));
        ValidateObjectId(userId, nameof(userId));

        var filter = Builders<Review>.Filter.And(
            Builders<Review>.Filter.Eq(review => review.MovieId, movieId),
            Builders<Review>.Filter.Eq(review => review.UserId, userId),
            Builders<Review>.Filter.Eq(review => review.Status, "Active"));

        return _reviews.Find(session, filter).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task InsertAsync(
        IClientSessionHandle session,
        Review review,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(review);

        ValidateObjectId(review.MovieId, nameof(review.MovieId));
        ValidateObjectId(review.UserId, nameof(review.UserId));

        if (review.Id is not null)
        {
            throw new ArgumentException("A new review must not have an ID assigned.",nameof(review));
        }

        review.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();

        await _reviews.InsertOneAsync(
            session,
            review,
            cancellationToken: cancellationToken);
    }

    public Task<Review?> UpdateAsync(
        IClientSessionHandle session,
        string movieId,
        string reviewId,
        string userId,
        long expectedVersion,
        long dataVersion,
        int newRating,
        string newReviewText,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        ValidateObjectId(movieId, nameof(movieId));
        ValidateObjectId(reviewId, nameof(reviewId));
        ValidateObjectId(userId, nameof(userId));

        if (expectedVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        }

        var filter = Builders<Review>.Filter.And(
            Builders<Review>.Filter.Eq(review => review.Id, reviewId),
            Builders<Review>.Filter.Eq(review => review.MovieId, movieId),
            Builders<Review>.Filter.Eq(review => review.UserId, userId),
            Builders<Review>.Filter.Eq(review => review.Status, "Active"),
            Builders<Review>.Filter.Eq(review => review.Version, expectedVersion));

        var update = Builders<Review>.Update
            // User-submitted data
            .Set(review => review.Rating, newRating)
            .Set(review => review.ReviewText, newReviewText)

            // Aggregation state
            .Set(review => review.DataVersion, dataVersion)
            .Set(review => review.ProcessedVersion, dataVersion)
            .Set(review => review.AppliedRating, newRating)

            // Immediate mode has no pending work or active lease
            .Set(review => review.PendingSinceUtc, null)
            .Set(review => review.ProcessingLeaseToken, null)
            .Set(review => review.ProcessingLeaseExpiresAtUtc, null)
            .Set(review => review.ProcessingAttemptCount, 0)

            // Audit and optimistic concurrency
            .Set(review => review.UpdatedAtUtc, updatedAtUtc)
            .Inc(review => review.Version, 1);

        return _reviews.FindOneAndUpdateAsync(session,filter,update,
            new FindOneAndUpdateOptions<Review>
            {
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);
    }

    public Task<Review?> DeleteAsync(
        IClientSessionHandle session,
        string movieId,
        string reviewId,
        string userId,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        ValidateObjectId(movieId, nameof(movieId));
        ValidateObjectId(reviewId, nameof(reviewId));
        ValidateObjectId(userId, nameof(userId));

        if (expectedVersion < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedVersion));
        }

        var filter = Builders<Review>.Filter.And(
            Builders<Review>.Filter.Eq(review => review.Id, reviewId),
            Builders<Review>.Filter.Eq(review => review.MovieId, movieId),
            Builders<Review>.Filter.Eq(review => review.UserId, userId),
            Builders<Review>.Filter.Eq(review => review.Status, "Active"),
            Builders<Review>.Filter.Eq(review => review.Version, expectedVersion));

        return _reviews.FindOneAndDeleteAsync<Review?>(session,filter,cancellationToken: cancellationToken); //<Review?> the complier thaught that it will retun movies but we gave that has the nullable so we need explicitly define <Review?>
    }

    private static void ValidateObjectId(
        string value,
        string parameterName)
    {
        if (!MongoDB.Bson.ObjectId.TryParse(value, out _))
        {
            throw new ArgumentException("A valid MongoDB ObjectId is required.",parameterName);
        }
    }




    //public async Task<bool> MarkContributionAppliedAsync(
    //IClientSessionHandle session,
    //string movieId,
    //string reviewId,
    //long expectedVersion,
    //int appliedRating,
    //long processedVersion,
    //CancellationToken cancellationToken = default)
    //{
    //    ArgumentNullException.ThrowIfNull(session);

    //    ValidateObjectId(movieId, nameof(movieId));
    //    ValidateObjectId(reviewId, nameof(reviewId));

    //    if (expectedVersion < 1)
    //    {
    //        throw new ArgumentOutOfRangeException(nameof(expectedVersion));
    //    }

    //    if (appliedRating is < 1 or > 10)
    //    {
    //        throw new ArgumentOutOfRangeException(nameof(appliedRating));
    //    }

    //    if (processedVersion < 1)
    //    {
    //        throw new ArgumentOutOfRangeException(nameof(processedVersion));
    //    }

    //    var filter = Builders<Review>.Filter.And(
    //        Builders<Review>.Filter.Eq(review => review.Id, reviewId),
    //        Builders<Review>.Filter.Eq(review => review.MovieId, movieId),
    //        Builders<Review>.Filter.Eq(review => review.Status, "Active"),
    //        Builders<Review>.Filter.Eq(review => review.Version, expectedVersion),
    //        Builders<Review>.Filter.Eq(review => review.DataVersion, processedVersion));

    //    var update = Builders<Review>.Update
    //        .Set(review => review.AppliedRating, appliedRating)
    //        .Set(review => review.ProcessedVersion, processedVersion);

    //    var result = await _reviews.UpdateOneAsync(
    //        session,
    //        filter,
    //        update,
    //        cancellationToken: cancellationToken);

    //    return result.ModifiedCount == 1;
    //}


    
    // InsertDeferredAsync without session for deferred mode
    public async Task InsertDeferredAsync(
    Review review,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);

        ValidateObjectId(review.MovieId, nameof(review.MovieId));
        ValidateObjectId(review.UserId, nameof(review.UserId));

        if (review.Id is not null)
        {
            throw new ArgumentException("A new review must not have an ID assigned.",nameof(review));
        }

        if (review.CreatedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("CreatedAtUtc must be UTC.",nameof(review));
        }

        review.Id = ObjectId.GenerateNewId().ToString();

        review.Status = "Active";
        review.Version = 1;

        review.DataVersion = 1;
        review.ProcessedVersion = 0;
        review.AppliedRating = null;

        review.PendingSinceUtc = review.CreatedAtUtc;

        review.ProcessingLeaseToken = null;
        review.ProcessingLeaseExpiresAtUtc = null;
        review.ProcessingAttemptCount = 0;

        await _reviews.InsertOneAsync(review,cancellationToken: cancellationToken);
    }


    // UpdateDeferredAsync without session for deferred mode
    public Task<Review?> UpdateDeferredAsync(
    string movieId,
    string reviewId,
    string userId,
    long expectedVersion,
    long dataVersion,
    bool ratingChanged,
    int newRating,
    string newReviewText,
    DateTime? pendingSinceUtc,
    DateTime updatedAtUtc,
    CancellationToken cancellationToken = default)
    {
        ValidateObjectId(movieId, nameof(movieId));
        ValidateObjectId(reviewId, nameof(reviewId));
        ValidateObjectId(userId, nameof(userId));

        if (expectedVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        }

        if (newRating is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(newRating));
        }

        if (dataVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dataVersion));
        }

        if (updatedAtUtc.Kind != DateTimeKind.Utc ||(pendingSinceUtc.HasValue && pendingSinceUtc.Value.Kind != DateTimeKind.Utc))
        {
            throw new ArgumentException("Timestamps must be UTC.");
        }

        var filter = Builders<Review>.Filter.And(
            Builders<Review>.Filter.Eq(review => review.Id, reviewId),
            Builders<Review>.Filter.Eq(review => review.MovieId, movieId),
            Builders<Review>.Filter.Eq(review => review.UserId, userId),
            Builders<Review>.Filter.Eq(review => review.Status, "Active"),
            Builders<Review>.Filter.Eq(review => review.Version, expectedVersion));


        var update = Builders<Review>.Update
        .Set(review => review.Rating, newRating)
        .Set(review => review.ReviewText, newReviewText)
        .Set(review => review.UpdatedAtUtc, updatedAtUtc)
        .Inc(review => review.Version, 1);

        if (ratingChanged)
        {
            update = update
                .Set(review => review.DataVersion, dataVersion)
                .Set(review => review.PendingSinceUtc, pendingSinceUtc);
        }

        return _reviews.FindOneAndUpdateAsync(filter,update,
            new FindOneAndUpdateOptions<Review>
            {
                ReturnDocument = ReturnDocument.After
            },cancellationToken);
    }

    

    // MarkForDeferredDeletionAsync without session for deferred mode
    public Task<Review?> MarkForDeferredDeletionAsync(
    string movieId,
    string reviewId,
    string userId,
    long expectedVersion,
    long dataVersion,
    DateTime pendingSinceUtc,
    DateTime updatedAtUtc,
    CancellationToken cancellationToken = default)
    {
        ValidateObjectId(movieId, nameof(movieId));
        ValidateObjectId(reviewId, nameof(reviewId));
        ValidateObjectId(userId, nameof(userId));

        if (expectedVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        }

        if (dataVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dataVersion));
        }

        if (pendingSinceUtc.Kind != DateTimeKind.Utc || updatedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamps must be UTC.");
        }

        var filter = Builders<Review>.Filter.And(
            Builders<Review>.Filter.Eq(review => review.Id, reviewId),
            Builders<Review>.Filter.Eq(review => review.MovieId, movieId),
            Builders<Review>.Filter.Eq(review => review.UserId, userId),
            Builders<Review>.Filter.Eq(review => review.Status, "Active"),
            Builders<Review>.Filter.Eq(review => review.Version, expectedVersion));

        var update = Builders<Review>.Update
            .Set(review => review.Status, "PendingDeletion")
            .Set(review => review.DataVersion, dataVersion)
            .Set(review => review.PendingSinceUtc, pendingSinceUtc)
            .Set(review => review.UpdatedAtUtc, updatedAtUtc)
            .Inc(review => review.Version, 1);

        return _reviews.FindOneAndUpdateAsync(filter,update,
            new FindOneAndUpdateOptions<Review>
            {
                ReturnDocument = ReturnDocument.After
            },cancellationToken);
    }

    public async Task<IReadOnlyList<Review>> ClaimPendingBatchAsync(
        int batchSize,
        DateTime nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (batchSize < 1 || batchSize > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize),"Batch size must be between 1 and 500.");
        }

        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The timestamp must be UTC.",nameof(nowUtc));
        }

        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration),"Lease duration must be greater than zero.");
        }

        var filterBuilder = Builders<Review>.Filter;

        var pendingFilter = filterBuilder.Type(review => review.PendingSinceUtc,BsonType.DateTime);

        // The aggregation worker can process active reviews and review deletions.
        var eligibleStatus = filterBuilder.In(review => review.Status,new[] { "Active", "PendingDeletion" });

        var sort = Builders<Review>.Sort
            .Ascending(review => review.PendingSinceUtc)
            .Ascending(review => review.Id);

        var claimedReviews = new List<Review>(batchSize);

        // Preserve the caller's time as the starting point while allowing
        // each claim to have a fresh timestamp.
        var actualStartTimeUtc = DateTime.UtcNow;

        for (var i = 0; i < batchSize; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var claimTimeUtc = nowUtc.Add(DateTime.UtcNow - actualStartTimeUtc);
            var leaseToken = Guid.NewGuid().ToString("N");

            var leaseAvailable = filterBuilder.Or(filterBuilder.Eq(review => review.ProcessingLeaseToken,null),

                filterBuilder.Eq(review => review.ProcessingLeaseExpiresAtUtc,null),

                filterBuilder.Lte(review => review.ProcessingLeaseExpiresAtUtc,claimTimeUtc));

            var claimFilter = filterBuilder.And(pendingFilter,eligibleStatus,leaseAvailable);

            var update = Builders<Review>.Update
                .Set(review => review.ProcessingLeaseToken,leaseToken)
                .Set(review => review.ProcessingLeaseExpiresAtUtc,claimTimeUtc.Add(leaseDuration))
                .Inc(review => review.ProcessingAttemptCount,1);

            // MongoDB atomically selects and updates one matching review.
            var claimedReview = await _reviews.FindOneAndUpdateAsync(claimFilter,update,
                new FindOneAndUpdateOptions<Review>
                {
                    Sort = sort,
                    ReturnDocument = ReturnDocument.After
                },cancellationToken);

            // No eligible review remains.
            if (claimedReview is null) //null here is no review found, and we are not using the exception because we want to return the list of claimed reviews so far
            {
                break;
            }

            claimedReviews.Add(claimedReview);
        }

        return claimedReviews;
    }


    public Task<Review?> GetClaimedByIdAsync(
     IClientSessionHandle session,
     string reviewId,
     string leaseToken,
     DateTime nowUtc,
     CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        ValidateObjectId(reviewId, nameof(reviewId));

        if (string.IsNullOrWhiteSpace(leaseToken))
        {
            throw new ArgumentException("A lease token is required.",nameof(leaseToken));
        }

        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The timestamp must be UTC.",nameof(nowUtc));
        }

        var filter = Builders<Review>.Filter.And(
            Builders<Review>.Filter.Eq(review => review.Id,reviewId),
            Builders<Review>.Filter.Eq(review => review.ProcessingLeaseToken,leaseToken),

            Builders<Review>.Filter.Gt(review => review.ProcessingLeaseExpiresAtUtc,nowUtc),

            Builders<Review>.Filter.In(review => review.Status,new[] { "Active", "PendingDeletion" }));

        return _reviews.Find(session, filter).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> CompleteClaimedAsync(
    IClientSessionHandle session,
    string reviewId,
    string leaseToken,
    long expectedDataVersion,
    int appliedRating,
    DateTime nowUtc,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        ValidateObjectId(reviewId, nameof(reviewId));

        if (string.IsNullOrWhiteSpace(leaseToken))
        {
            throw new ArgumentException("A lease token is required.",nameof(leaseToken));
        }

        if (expectedDataVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedDataVersion));
        }

        if (appliedRating < 1 || appliedRating > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(appliedRating),"Rating must be between 1 and 10.");
        }

        var filter = Builders<Review>.Filter.And(
            Builders<Review>.Filter.Eq(review => review.Id,reviewId),
            Builders<Review>.Filter.Eq(review => review.Status,"Active"),
            Builders<Review>.Filter.Eq(review => review.ProcessingLeaseToken,leaseToken),
            Builders<Review>.Filter.Gt(review => review.ProcessingLeaseExpiresAtUtc,nowUtc),
            Builders<Review>.Filter.Eq(review => review.DataVersion,expectedDataVersion));

        var update = Builders<Review>.Update
            .Set(review => review.AppliedRating, appliedRating)
            .Set(review => review.ProcessedVersion, expectedDataVersion)
            .Set(review => review.PendingSinceUtc, null)
            .Set(review => review.ProcessingLeaseToken, null)
            .Set(review => review.ProcessingLeaseExpiresAtUtc, null)
            .Set(review => review.ProcessingAttemptCount, 0);
            //.Set(review => review.UpdatedAtUtc, nowUtc);

        var result = await _reviews.UpdateOneAsync(
            session,
            filter,
            update,
            cancellationToken: cancellationToken);

        return result.MatchedCount == 1;
    }

    public async Task<bool> DeleteClaimedAsync(
     IClientSessionHandle session,
     string reviewId,
     string leaseToken,
     long expectedDataVersion,
     DateTime nowUtc,
     CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        ValidateObjectId(reviewId, nameof(reviewId));

        if (string.IsNullOrWhiteSpace(leaseToken))
        {
            throw new ArgumentException("A lease token is required.",nameof(leaseToken));
        }

        if (expectedDataVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedDataVersion));
        }

        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The timestamp must be UTC.",nameof(nowUtc));
        }

        var filter = Builders<Review>.Filter.And(
            Builders<Review>.Filter.Eq(review => review.Id,reviewId),
            Builders<Review>.Filter.Eq(review => review.Status,"PendingDeletion"),
            Builders<Review>.Filter.Eq(review => review.ProcessingLeaseToken,leaseToken),
            Builders<Review>.Filter.Gt(review => review.ProcessingLeaseExpiresAtUtc,nowUtc),
            Builders<Review>.Filter.Eq(review => review.DataVersion,expectedDataVersion));

        var result = await _reviews.DeleteOneAsync(session,filter,cancellationToken: cancellationToken);

        return result.DeletedCount == 1;
    }
    public async Task<IReadOnlyList<Review>> GetNextPurgeBatchAsync(
    IClientSessionHandle session,
    string movieId,
    ObjectId? lastProcessedReviewId,
    int batchSize,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        ValidateObjectId(movieId, nameof(movieId));

        if (batchSize < 1 || batchSize > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize),"Batch size must be between 1 and 500.");
        }

        var filterBuilder = Builders<Review>.Filter;

        var filter = filterBuilder.Eq(review => review.MovieId,movieId);

        if (lastProcessedReviewId.HasValue)
        {
            filter &= filterBuilder.Gt(review => review.Id,lastProcessedReviewId.Value.ToString());
        }

        return await _reviews
            .Find(session, filter)
            .SortBy(review => review.Id)
            .Limit(batchSize)
            .ToListAsync(cancellationToken);
    }
    public async Task<long> DeletePurgeBatchAsync(
    IClientSessionHandle session,
    string movieId,
    IReadOnlyCollection<string> reviewIds,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewIds);

        ValidateObjectId(movieId, nameof(movieId));

        if (reviewIds.Count == 0)
        {
            return 0;
        }

        foreach (var reviewId in reviewIds)
        {
            ValidateObjectId(reviewId, nameof(reviewIds));
        }

        var filter = Builders<Review>.Filter.And(
            Builders<Review>.Filter.Eq(review => review.MovieId,movieId),
            Builders<Review>.Filter.In(review => review.Id,reviewIds));

        var result = await _reviews.DeleteManyAsync(session,filter,cancellationToken: cancellationToken);

        return result.DeletedCount;
    }

    public Task<long> CountAllByMovieAsync(
    IClientSessionHandle session,
    string movieId,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!ObjectId.TryParse(movieId, out _))
            throw new ArgumentException("Invalid movie ID.", nameof(movieId));

        var filter = Builders<Review>.Filter.Eq(
            review => review.MovieId,
            movieId);

        return _reviews.CountDocumentsAsync(
            session,
            filter,
            cancellationToken: cancellationToken);
    }
}

