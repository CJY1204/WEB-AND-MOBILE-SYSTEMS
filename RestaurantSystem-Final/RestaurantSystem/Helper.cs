using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity; // Use Microsoft native hashing (Slide 11)
using System.Security.Claims;

namespace RestaurantSystem 
{
    public class Helper
    {
        private readonly IWebHostEnvironment en;
        private readonly IHttpContextAccessor ct;
        private readonly PasswordHasher<object> ph = new(); // Follow Slide 11 pattern

        public Helper(IWebHostEnvironment en, IHttpContextAccessor ct)
        {
            this.en = en;
            this.ct = ct;
        }

        // ==========================================
        // Password hashing and verification (Slide 11)
        // ==========================================
        public string HashPassword(string password)
        {
            return ph.HashPassword(0, password); // Follow Slide 11
        }

        public bool VerifyPassword(string hash, string password)
        {
            return ph.VerifyHashedPassword(0, hash, password) == PasswordVerificationResult.Success; // Follow Slide 11
        }

        // ==========================================
        // Cookie sign-in and sign-out (Slide 12, 13)
        // ==========================================
        public void SignIn(string username, string role, bool rememberMe)
        {
            // (1) Claim, identity and principal
            List<Claim> claims = new()
            {
                new(ClaimTypes.Name, username), // Stores the Username
                new(ClaimTypes.Role, role)      // Stores the user's Role (ADMIN / MEMBER)
            };

            ClaimsIdentity identity = new(claims, CookieAuthenticationDefaults.AuthenticationScheme); // Default "Cookies" scheme (Slide 12)
            ClaimsPrincipal principal = new(identity);

            // (2) Remember me
            AuthenticationProperties properties = new()
            {
                IsPersistent = rememberMe
            };

            // (3) Sign in
            ct.HttpContext!.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties); // Follow Slide 12
        }

        public void SignOut()
        {
            ct.HttpContext!.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); // Follow Slide 13

            // Clear all Session data for the current user (cart data reset)
            ct.HttpContext!.Session.Clear();
        }

        // ==========================================
        // Generate random reset password (Slide 14)
        // ==========================================
        public string RandomPassword()
        {
            string s = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"; // Follow Slide 14
            string password = "";
            Random r = new();
            for (int i = 1; i <= 10; i++)
            {
                password += s[r.Next(s.Length)];
            }
            return password;
        }
    }
}
