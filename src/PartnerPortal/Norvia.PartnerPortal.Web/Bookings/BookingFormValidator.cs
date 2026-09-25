using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Norvia.PartnerPortal.Web.Bookings;

/// <summary>
/// Checks the booking form with <see cref="BookingFormRules"/> and puts each problem on its own field, the goods lines'
/// included, so its message shows next to it.
/// </summary>
public sealed class BookingFormValidator : ComponentBase, IDisposable
{
    private EditContext? _editContext;
    private ValidationMessageStore? _messages;

    [CascadingParameter]
    public EditContext? CurrentEditContext { get; set; }

    protected override void OnParametersSet()
    {
        if (CurrentEditContext == _editContext)
        {
            return;
        }

        Detach();
        _editContext = CurrentEditContext ?? throw new InvalidOperationException($"{nameof(BookingFormValidator)} must be inside an EditForm.");
        _messages = new ValidationMessageStore(_editContext);
        _editContext.OnValidationRequested += Validate;
        _editContext.OnFieldChanged += Revalidate;
    }

    public void Dispose() => Detach();

    private void Validate(object? sender, ValidationRequestedEventArgs e) => Check();

    // Once a field has a message, fixing it clears the message straight away.
    private void Revalidate(object? sender, FieldChangedEventArgs e) => Check();

    private void Check()
    {
        _messages!.Clear();
        foreach (var problem in BookingFormRules.Check((BookingForm)_editContext!.Model))
        {
            _messages.Add(new FieldIdentifier(problem.Model, problem.Field), problem.Message);
        }

        _editContext.NotifyValidationStateChanged();
    }

    private void Detach()
    {
        if (_editContext is null)
        {
            return;
        }

        _editContext.OnValidationRequested -= Validate;
        _editContext.OnFieldChanged -= Revalidate;
        _messages?.Clear();
    }
}
