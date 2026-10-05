using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace RestaurantSystem.Models
{
    public class ViewModels
    {
        // Login ViewModel
        public class LoginVM
        {
            [Required(ErrorMessage = "Please enter username")]
            public required string Username { get; set; }

            [Required(ErrorMessage = "Please enter password")]
            [DataType(DataType.Password)]
            public required string Password { get; set; }

            public bool RememberMe { get; set; }
        }

        // Register ViewModel
        public class RegisterVM
        {
            [Required(ErrorMessage = "Please enter username")]
            [StringLength(50)]
            public required string Username { get; set; }

            [Required(ErrorMessage = "Please enter email address")]
            [EmailAddress]
            [StringLength(100)]
            public required string Email { get; set; }

            [Required(ErrorMessage = "Please enter password")]
            [StringLength(100, MinimumLength = 6)]
            [DataType(DataType.Password)]
            public required string Password { get; set; }

            [Required(ErrorMessage = "Please enter full name")]
            [StringLength(100)]
            public required string FullName { get; set; }

            [Phone]
            [StringLength(20)]
            public string? PhoneNumber { get; set; }
        }

        // Reset Password ViewModel — used by logged-in members on the Profile page.
        public class ResetPasswordVM
        {
            [Required(ErrorMessage = "Please enter your current password")]
            [DataType(DataType.Password)]
            public required string OldPassword { get; set; }

            [Required(ErrorMessage = "Please enter a new password")]
            [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be between 6 and 100 characters")]
            [DataType(DataType.Password)]
            public required string NewPassword { get; set; }

            [Required(ErrorMessage = "Please confirm your new password")]
            [DataType(DataType.Password)]
            [Compare("NewPassword", ErrorMessage = "The confirmation password does not match.")]
            public required string ConfirmNewPassword { get; set; }
        }

        // Step 1 of Password Recovery (Email)
        public class PasswordRecoveryVM
        {
            [Required(ErrorMessage = "Please enter your email address")]
            [EmailAddress(ErrorMessage = "Please enter a valid email address")]
            public required string Email { get; set; }
        }

        // Step 2 of Password Recovery (Email)
        public class PasswordRecoveryResetVM
        {
            [Required]
            public required string Token { get; set; }

            [Required]
            public required string Email { get; set; }

            [Required(ErrorMessage = "Please enter a new password")]
            [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be between 6 and 100 characters")]
            [DataType(DataType.Password)]
            public required string NewPassword { get; set; }

            [Required(ErrorMessage = "Please confirm your new password")]
            [DataType(DataType.Password)]
            [Compare("NewPassword", ErrorMessage = "The confirmation password does not match.")]
            public required string ConfirmNewPassword { get; set; }
        }

        // Edit Profile ViewModel
        public class EditProfileVM
        {
            [Required(ErrorMessage = "Please enter full name")]
            [StringLength(100, ErrorMessage = "Full name cannot exceed 100 characters")]
            public required string FullName { get; set; }

            [Required(ErrorMessage = "Please enter email address")]
            [EmailAddress(ErrorMessage = "Invalid email address format")]
            [StringLength(100, ErrorMessage = "Email cannot exceed 100 characters")]
            public required string Email { get; set; }

            [Phone(ErrorMessage = "Invalid phone number format")]
            [StringLength(20)]
            public string? PhoneNumber { get; set; }

            [StringLength(250, ErrorMessage = "Address cannot exceed 250 characters")]
            public string? Address { get; set; }
        }


        // Represents a single item in the shopping cart
        public class CartItemVM
        {
            public int ProductId { get; set; }
            public required string ProductName { get; set; }
            public decimal Price { get; set; }
            public int Quantity { get; set; }

            public decimal Total => Price * Quantity;
        }

        // Represents one row in the Top Selling Products ranking (Home page hero carousel)
        public class TopSellingItemVM
        {
            public int ProductId { get; set; }
            public required string ProductName { get; set; }
            public required string ImageUrl { get; set; }
            public int CategoryId { get; set; }
            public required string CategoryName { get; set; }
            public int TotalSold { get; set; }
        }

        // ==========================================
        // Order / Checkout ViewModels
        // ==========================================

        // Step 1: Takeaway vs Dine-in (+ table selection)
        public class CheckoutVM
        {
            public List<CartItemVM> CartItems { get; set; } = new();

            [Required(ErrorMessage = "Please choose Takeaway or Dine-in")]
            public required string DiningOption { get; set; } // "Takeaway" or "DineIn"

            public int? TableId { get; set; }

            public List<Table> AvailableTables { get; set; } = new();

            // Only required when DiningOption == "Takeaway" — validated manually
            // in the controller since it depends on which option was picked.
            [StringLength(250, ErrorMessage = "Address cannot exceed 250 characters")]
            public string? Address { get; set; }

            public decimal GrandTotal => CartItems.Sum(i => i.Total);
        }

        // Step 2: Payment method selection
        public class PaymentVM
        {
            public List<CartItemVM> CartItems { get; set; } = new();

            // Nullable on purpose: the controller reads the real value from Session,
            // not from this form field, so it must NOT be implicitly treated as required
            // (a non-nullable string here would silently block every checkout with
            // "The DiningOption field is required.")
            public string? DiningOption { get; set; }
            public string? TableName { get; set; }

            [Required(ErrorMessage = "Please select a payment method")]
            public required string PaymentMethod { get; set; }

            public decimal GrandTotal => CartItems.Sum(i => i.Total);
        }

        // A single line item on a receipt / order summary
        public class OrderItemLineVM
        {
            public required string ProductName { get; set; }
            public int Quantity { get; set; }
            public decimal UnitPrice { get; set; }

            public decimal SubTotal => UnitPrice * Quantity;
        }

        // Full summary of a placed order — used by Confirmation, Details, History, and the PDF receipt
        public class OrderSummaryVM
        {
            public int OrderId { get; set; }
            public DateTime OrderDate { get; set; }
            public required string Status { get; set; }
            public decimal TotalAmount { get; set; }
            public required string DiningOption { get; set; }
            public string? TableName { get; set; }
            public required string PaymentMethod { get; set; }
            public string? DeliveryAddress { get; set; }
            public List<OrderItemLineVM> Items { get; set; } = new();

            // True only while the table this order used is still marked occupied —
            // drives whether "Check Out Table" shows up on My Orders / the receipt
            public bool TableStillOccupied { get; set; }

            // Matches the same rule OrderController.Cancel enforces server-side —
            // only a still-pending order can be cancelled by the customer
            public bool CanCancel => Status == "PENDING";
        }

        // ==========================================
        // Admin ViewModels
        // ==========================================

        // Used by Admin's UserCreate / UserEdit pages
        public class UserVM
        {
            public int UserId { get; set; }

            [Required(ErrorMessage = "Please enter username")]
            [StringLength(50)]
            public required string Username { get; set; }

            [Required(ErrorMessage = "Please enter email address")]
            [EmailAddress]
            [StringLength(100)]
            public required string Email { get; set; }

            [Required(ErrorMessage = "Please enter full name")]
            [StringLength(100)]
            public required string FullName { get; set; }

            [Phone]
            [StringLength(20)]
            public string? PhoneNumber { get; set; }

            [StringLength(250, ErrorMessage = "Address cannot exceed 250 characters")]
            public string? Address { get; set; }

            [Required]
            [StringLength(20)]
            public string Role { get; set; } = "MEMBER";

            // Required when creating a new user; leave blank on edit to keep the current password
            [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be between 6 and 100 characters")]
            [DataType(DataType.Password)]
            public string? Password { get; set; }

            public bool IsBlocked { get; set; }
        }

        // One row in Admin's order management table (Admin/Orders)
        public class OrderVM
        {
            public int OrderId { get; set; }
            public required string CustomerName { get; set; }
            public string? TableName { get; set; }
            public DateTime OrderDate { get; set; }
            public decimal TotalAmount { get; set; }
            public required string OrderStatus { get; set; }
            public List<OrderDetailVM> Details { get; set; } = new();
        }

        // A single line item within OrderVM
        public class OrderDetailVM
        {
            public required string ProductName { get; set; }
            public int Quantity { get; set; }
            public decimal PriceAtOrder { get; set; }

            public decimal Subtotal => Quantity * PriceAtOrder;
        }

        // ==========================================
        // Report ViewModels
        // ==========================================

        public class ReportItemVM
        {
            public int ProductId { get; set; }
            public required string ProductName { get; set; }
            public int Quantity { get; set; }
            public decimal Revenue { get; set; }
        }

        public class DailyReportVM
        {
            public DateTime Date { get; set; }
            public int TotalOrders { get; set; }
            public decimal TotalRevenue { get; set; }
            public decimal AvgOrderValue { get; set; }
            public List<ReportItemVM> Items { get; set; } = new();
        }

        public class DaySummaryVM
        {
            public DateTime Day { get; set; }
            public int Orders { get; set; }
            public decimal Revenue { get; set; }
        }

        public class MonthlyReportVM
        {
            public int Year { get; set; }
            public int Month { get; set; }
            public required string MonthName { get; set; }
            public int TotalOrders { get; set; }
            public decimal TotalRevenue { get; set; }
            public List<DaySummaryVM> DailySummaries { get; set; } = new();
            public List<ReportItemVM> TopItems { get; set; } = new();
        }

        public class TopSellingVM
        {
            public DateTime? From { get; set; }
            public DateTime? To { get; set; }
            public int TotalOrders { get; set; }
            public decimal TotalRevenue { get; set; }
            public List<ReportItemVM> Items { get; set; } = new();
        }
    }
}
