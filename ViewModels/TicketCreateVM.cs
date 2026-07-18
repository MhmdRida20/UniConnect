using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;
using UniConnect.Models;

namespace UniConnect.ViewModels
{
    public class TicketCreateVM
    {
        [Required(ErrorMessage = "Please choose a category.")]
        [Display(Name = "Category")]
        public int CategoryId { get; set; }

        [Required(ErrorMessage = "Please enter a title.")]
        [StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter a description.")]
        [StringLength(2000)]
        public string Description { get; set; } = string.Empty;

        [Required]
        public TicketPriority Priority { get; set; } = TicketPriority.Medium;

        // Optional file attachment (FR-26, FR-32). Size/type validated in the
        // controller, since [DataType]/[StringLength] don't apply to IFormFile.
        public IFormFile? Attachment { get; set; }

        // Set to true on resubmission after the user confirms past a
        // duplicate-ticket warning (Edge Case: "Duplicate ticket submission").
        public bool ConfirmDuplicate { get; set; }

        // View-only — populates the category dropdown. Never actually posted
        // back by the browser, so it must be excluded from both binding and
        // validation, or ASP.NET Core treats it as a required form field that
        // was never filled in (which silently blocked every submission).
        [BindNever]
        [ValidateNever]
        public SelectList AvailableCategories { get; set; } = default!;
    }
}
