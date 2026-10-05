using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Models;
//using RestaurantSystem.Services;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using static RestaurantSystem.Models.ViewModels;

namespace RestaurantSystem.Controllers
{
    public class AccountController : Controller
    {
        private readonly RestaurantDbContext db;
        private readonly Helper hp;
        private readonly IEmailService emailService;
        private readonly IWebHostEnvironment env;

        public AccountController(RestaurantDbContext db, Helper hp, IEmailService emailService, IWebHostEnvironment env)
        {
            this.db = db;
            this.hp = hp;
            this.emailService = emailService;
            this.env = env;
        }

        // ==========================================
        // [GET/POST: Account/Register]
        // ==========================================
        [HttpGet]
        public IActionResult Register()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        [HttpPost]
        public IActionResult Register(ViewModels.RegisterVM vm)
        {
            if (ModelState.IsValid)
            {
                bool isUsernameExist = db.Users.Any(u => u.Username == vm.Username);
                if (isUsernameExist)
                {
                    ModelState.AddModelError("Username", "This username is already taken.");
                    return View(vm);
                }

                bool isEmailExist = db.Users.Any(u => u.Email == vm.Email);
                if (isEmailExist)
                {
                    ModelState.AddModelError("Email", "This email address is already registered.");
                    return View(vm);
                }

                var newUser = new User
                {
                    Username = vm.Username,
                    Email = vm.Email,
                    FullName = vm.FullName,
                    PhoneNumber = vm.PhoneNumber ?? "",
                    Role = "MEMBER",
                    Password = hp.HashPassword(vm.Password)
                };

                db.Users.Add(newUser);
                db.SaveChanges();

                TempData["Info"] = "Registration successful! You can now log in.";
                return RedirectToAction("Login");
            }

            return View(vm);
        }

        // ==========================================
        // [GET/POST: Login]
        // ==========================================
        [HttpGet]
        public IActionResult Login(string? returnURL)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                if (User.IsInRole("ADMIN"))
                {
                    return RedirectToAction("Dashboard", "Admin");
                }
                return RedirectToAction("Index", "Home");
            }

            ViewBag.ReturnURL = returnURL;
            return View();
        }

        [HttpPost]
        public IActionResult Login(ViewModels.LoginVM vm, string? returnURL)
        {
            if (ModelState.IsValid)
            {
                var u = db.Users.FirstOrDefault(x => x.Username == vm.Username);

                if (u != null && hp.VerifyPassword(u.Password, vm.Password))
                {
                    if (u.IsBlocked)
                    {
                        ModelState.AddModelError("", "This account has been blocked. Please contact the restaurant if you believe this is a mistake.");
                        ViewBag.ReturnURL = returnURL;
                        return View(vm);
                    }

                    TempData["Info"] = "Login successfully.";

                    hp.SignIn(u.Username, u.Role, vm.RememberMe);

                    if (!string.IsNullOrEmpty(returnURL))
                    {
                        return LocalRedirect(returnURL);
                    }

                    if (u.Role == "ADMIN")
                    {
                        return RedirectToAction("Dashboard", "Admin");
                    }
                    else
                    {
                        return RedirectToAction("Index", "Home");
                    }
                }

                ViewBag.Error = "Invalid Username or Password";
            }

            ViewBag.ReturnURL = returnURL;
            return View(vm);
        }


        // ==========================================
        // [POST: Logout]
        // ==========================================
        [HttpPost]
        public IActionResult Logout()
        {
            hp.SignOut();
            TempData["Info"] = "Logout successfully.";
            return RedirectToAction("Index", "Home");
        }


        // ==========================================
        // [GET/POST: ResetPassword] — for logged-in members only.
        // Requires the correct current password before setting a new one.
        // Reached via a button on the Profile page.
        // ==========================================
        [HttpGet]
        [Authorize]
        public IActionResult ResetPassword()
        {
            return View();
        }

        [HttpPost]
        [Authorize]
        public IActionResult ResetPassword(ResetPasswordVM vm)
        {
            if (ModelState.IsValid)
            {
                var username = User.Identity?.Name;
                var u = db.Users.FirstOrDefault(x => x.Username == username);

                if (u == null)
                {
                    ModelState.AddModelError("", "Account not found.");
                    return View(vm);
                }

                if (!hp.VerifyPassword(u.Password, vm.OldPassword))
                {
                    ModelState.AddModelError("OldPassword", "Current password is incorrect.");
                    return View(vm);
                }

                if (vm.OldPassword == vm.NewPassword)
                {
                    ModelState.AddModelError("NewPassword", "New password must be different from your current password.");
                    return View(vm);
                }

                u.Password = hp.HashPassword(vm.NewPassword);
                db.SaveChanges();

                TempData["Info"] = "Password changed successfully.";
                return RedirectToAction("Profile");
            }

            return View(vm);
        }

        // ==========================================
        // [GET/POST: PasswordRecovery] — Step 1: request a reset link by email.
        // A real email is sent via IEmailService/MailKit to whatever address the user typed in.
        // ==========================================
        [HttpGet]
        public IActionResult PasswordRecovery()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> PasswordRecovery(PasswordRecoveryVM vm)
        {
            if (ModelState.IsValid)
            {
                var u = db.Users.FirstOrDefault(x => x.Email == vm.Email);

                if (u == null)
                {
                    ViewBag.Info = "If an account with that email exists, a password reset link has been sent to it.";
                    return View(vm);
                }

                u.PasswordResetToken = GenerateResetToken();
                u.PasswordResetTokenExpiry = DateTime.Now.AddMinutes(30);
                db.SaveChanges();

                var resetLink = Url.Action("PasswordRecoveryReset", "Account",
                    new { token = u.PasswordResetToken, email = u.Email }, Request.Scheme);

                try
                {
                    if (!string.IsNullOrEmpty(resetLink))
                    {
                        await emailService.SendPasswordResetEmailAsync(u.Email, resetLink);
                    }
                    ViewBag.Info = "A password reset link has been sent to your email. Please check your inbox (and spam folder).";
                }
                catch (Exception)
                {
                    ViewBag.Info = "We couldn't send the email right now. Please try again later.";
                }

                return View(vm);
            }

            return View(vm);
        }

        // ==========================================
        // [GET/POST: PasswordRecoveryReset] — Step 2: the page the emailed link points to.
        // ==========================================
        [HttpGet]
        public IActionResult PasswordRecoveryReset(string token, string email)
        {
            var u = db.Users.FirstOrDefault(x => x.Email == email
                && x.PasswordResetToken == token
                && x.PasswordResetTokenExpiry != null
                && x.PasswordResetTokenExpiry > DateTime.Now);

            if (u == null)
            {
                TempData["Info"] = "This password reset link is invalid or has expired. Please request a new one.";
                return RedirectToAction("PasswordRecovery");
            }

            var vm = new PasswordRecoveryResetVM { Token = token, Email = email, NewPassword = "", ConfirmNewPassword = "" };
            return View(vm);
        }

        [HttpPost]
        public IActionResult PasswordRecoveryReset(PasswordRecoveryResetVM vm)
        {
            if (ModelState.IsValid)
            {
                var u = db.Users.FirstOrDefault(x => x.Email == vm.Email
                    && x.PasswordResetToken == vm.Token
                    && x.PasswordResetTokenExpiry != null
                    && x.PasswordResetTokenExpiry > DateTime.Now);

                if (u == null)
                {
                    TempData["Info"] = "This password reset link is invalid or has expired. Please request a new one.";
                    return RedirectToAction("PasswordRecovery");
                }

                if (hp.VerifyPassword(u.Password, vm.NewPassword))
                {
                    ModelState.AddModelError("NewPassword", "New password must be different from your current password.");
                    return View(vm);
                }

                u.Password = hp.HashPassword(vm.NewPassword);
                u.PasswordResetToken = null;
                u.PasswordResetTokenExpiry = null;
                db.SaveChanges();

                TempData["Info"] = "Your password has been reset successfully. You can now log in.";
                return RedirectToAction("Login");
            }

            return View(vm);
        }

        // ==========================================
        // [GET: Profile] — loads the real User record so the view can display it
        // ==========================================
        [Authorize]
        public IActionResult Profile()
        {
            var username = User.Identity?.Name;
            var u = db.Users.FirstOrDefault(x => x.Username == username);
            if (u == null)
            {
                return RedirectToAction("Login");
            }
            return View(u);
        }

        // ==========================================
        // [GET/POST: EditProfile] — update FullName / Email / PhoneNumber
        // ==========================================
        [HttpGet]
        [Authorize]
        public IActionResult EditProfile()
        {
            var username = User.Identity?.Name;
            var u = db.Users.FirstOrDefault(x => x.Username == username);
            if (u == null)
            {
                return RedirectToAction("Login");
            }

            var vm = new EditProfileVM
            {
                FullName = u.FullName,
                Email = u.Email,
                PhoneNumber = u.PhoneNumber,
                Address = u.Address
            };
            return View(vm);
        }

        [HttpPost]
        [Authorize]
        public IActionResult EditProfile(EditProfileVM vm)
        {
            if (ModelState.IsValid)
            {
                var username = User.Identity?.Name;
                var u = db.Users.FirstOrDefault(x => x.Username == username);
                if (u == null)
                {
                    return RedirectToAction("Login");
                }

                // If the email changed, make sure no OTHER account already uses it
                bool emailTaken = db.Users.Any(x => x.Email == vm.Email && x.UserId != u.UserId);
                if (emailTaken)
                {
                    ModelState.AddModelError("Email", "This email address is already in use by another account.");
                    return View(vm);
                }

                u.FullName = vm.FullName;
                u.Email = vm.Email;
                u.PhoneNumber = vm.PhoneNumber ?? "";
                u.Address = vm.Address;
                db.SaveChanges();

                TempData["Info"] = "Profile updated successfully.";
                return RedirectToAction("Profile");
            }

            return View(vm);
        }

        // ==========================================
        // [POST: UploadPhoto] — saves a new profile photo to wwwroot/images/avatars
        // ==========================================
        [HttpPost]
        [Authorize]
        public IActionResult UploadPhoto(IFormFile photo)
        {
            var username = User.Identity?.Name;
            var u = db.Users.FirstOrDefault(x => x.Username == username);
            if (u == null)
            {
                return RedirectToAction("Login");
            }

            if (photo == null || photo.Length == 0)
            {
                TempData["Info"] = "Please choose a photo to upload.";
                return RedirectToAction("Profile");
            }

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
            var extension = Path.GetExtension(photo.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(extension))
            {
                TempData["Info"] = "Only JPG or PNG files are allowed.";
                return RedirectToAction("Profile");
            }

            const long maxSizeBytes = 2 * 1024 * 1024; // 2MB, matches the hint text on the Profile page
            if (photo.Length > maxSizeBytes)
            {
                TempData["Info"] = "File size must not exceed 2MB.";
                return RedirectToAction("Profile");
            }

            var avatarsFolder = Path.Combine(env.WebRootPath, "images", "avatars");
            if (!Directory.Exists(avatarsFolder))
            {
                Directory.CreateDirectory(avatarsFolder);
            }

            // One file per user, named by UserId — re-uploading simply overwrites the old photo
            var fileName = $"user_{u.UserId}{extension}";
            var filePath = Path.Combine(avatarsFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                photo.CopyTo(stream);
            }

            u.ProfilePhotoUrl = $"/images/avatars/{fileName}";
            db.SaveChanges();

            TempData["Info"] = "Profile photo updated.";
            return RedirectToAction("Profile");
        }

        [Authorize]
        // Kept as a redirect so any existing links to Account/Orders still work —
        // the real order history now lives in OrderController.History
        public IActionResult Orders()
        {
            return RedirectToAction("History", "Order");
        }

        // ==========================================
        // [GET: Account/AccessDenied]
        // ==========================================
        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        // ==========================================
        // Private helpers
        // ==========================================
        private string GenerateResetToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToHexString(bytes);
        }
    }
}