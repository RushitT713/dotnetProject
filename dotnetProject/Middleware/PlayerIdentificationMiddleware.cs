using dotnetProject.Services;

namespace dotnetProject.Middleware
{
    public class PlayerIdentificationMiddleware
    {
        private readonly RequestDelegate _next;
        private const string PlayerIdCookieName = "FunPlayCasino_PlayerId";

        public PlayerIdentificationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, IWalletService walletService)
        {
            // Check if player ID cookie exists
            if (!context.Request.Cookies.TryGetValue(PlayerIdCookieName, out var playerId) ||
                string.IsNullOrEmpty(playerId))
            {
                // Generate new unique player ID
                playerId = Guid.NewGuid().ToString("N");

                // Set cookie (expires in 1 year)
                context.Response.Cookies.Append(PlayerIdCookieName, playerId, new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    HttpOnly = true,
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = false // Set to true in production with HTTPS
                });
            }

            // Store player ID in HttpContext items for easy access
            context.Items["PlayerId"] = playerId;

            // Ensure player exists in database
            await walletService.GetOrCreatePlayerAsync(playerId);

            await _next(context);
        }
    }

    // Extension method for easy middleware registration
    public static class PlayerIdentificationMiddlewareExtensions
    {
        public static IApplicationBuilder UsePlayerIdentification(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<PlayerIdentificationMiddleware>();
        }
    }
}