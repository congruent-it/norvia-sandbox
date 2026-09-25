using Norvia.PartnerPortal.Application.Bookings;

namespace Norvia.PartnerPortal.Web.Bookings;

/// <summary>
/// The booking form's model: what the party is filling in, bound to the inputs. It starts from a draft and becomes a
/// <see cref="BookingRequest"/> when it is sent. After Norvia rejects it, it still holds what was entered.
/// </summary>
public sealed class BookingForm
{
    public string? ShipperReference { get; set; }

    public AddressForm Collection { get; } = new();

    public DateTime? CollectFrom { get; set; }

    public DateTime? CollectUntil { get; set; }

    public ConsigneeForm Consignee { get; } = new();

    public AddressForm Delivery { get; } = new();

    public DateTime? DeliverFrom { get; set; }

    public DateTime? DeliverUntil { get; set; }

    public List<GoodsLineForm> GoodsLines { get; } = [];

    public string? Instructions { get; set; }

    public static BookingForm From(BookingRequest draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var form = new BookingForm
        {
            ShipperReference = draft.ShipperReference,
            CollectFrom = draft.CollectionWindow.From,
            CollectUntil = draft.CollectionWindow.Until,
            DeliverFrom = draft.DeliveryWindow.From,
            DeliverUntil = draft.DeliveryWindow.Until,
            Instructions = draft.Instructions,
        };
        form.Collection.Fill(draft.CollectionAddress);
        form.Delivery.Fill(draft.DeliveryAddress);
        form.Consignee.Fill(draft.Consignee);
        form.GoodsLines.AddRange(draft.GoodsLines.Select(GoodsLineForm.From));
        return form;
    }

    public BookingRequest ToRequest() => new()
    {
        ShipperReference = ShipperReference,
        CollectionAddress = Collection.ToAddress(),
        CollectionWindow = new LocalWindow(CollectFrom, CollectUntil),
        Consignee = Consignee.ToConsignee(),
        DeliveryAddress = Delivery.ToAddress(),
        DeliveryWindow = new LocalWindow(DeliverFrom, DeliverUntil),
        GoodsLines = [.. GoodsLines.Select(line => line.ToGoodsLine())],
        Instructions = Instructions,
    };

    public void AddGoodsLine() => GoodsLines.Add(new GoodsLineForm());

    public void RemoveGoodsLine(GoodsLineForm line) => GoodsLines.Remove(line);
}
