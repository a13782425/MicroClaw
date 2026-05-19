using System;
using System.ComponentModel.DataAnnotations;

namespace ShadUI.Demo.Validators;

public sealed class StartDateValidationAttribute(
    string matchProperty,
    string errorMessage = "Start date must be before end date"
) : ValidationAttribute(errorMessage)
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var instance = validationContext.ObjectInstance;
        var endDate = instance.GetType().GetProperty(matchProperty)?.GetValue(instance);

        if (endDate == null)
        {
            return ValidationResult.Success;
        }

        return ((DateOnly)value!).CompareTo(endDate) < 0
            ? ValidationResult.Success
            : new ValidationResult(ErrorMessage);
    }
}

public sealed class EndDateValidationAttribute(
    string matchProperty,
    string errorMessage = "End date must be after start date"
) : ValidationAttribute(errorMessage)
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var instance = validationContext.ObjectInstance;
        var startDate = instance.GetType().GetProperty(matchProperty)?.GetValue(instance);

        if (startDate == null)
        {
            return ValidationResult.Success;
        }

        return ((DateOnly)value!).CompareTo(startDate) > 0
            ? ValidationResult.Success
            : new ValidationResult(ErrorMessage);
    }
}