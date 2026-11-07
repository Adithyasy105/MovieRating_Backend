
using MovieRating.Models;

namespace MovieRating.Services;

public interface ITokenService
{
    AccessTokenResult CreateAccessToken(User user);
}
