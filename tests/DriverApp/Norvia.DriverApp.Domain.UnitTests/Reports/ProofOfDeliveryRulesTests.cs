using Norvia.DriverApp.Domain.Reports;

namespace Norvia.DriverApp.Domain.UnitTests.Reports;

/// <summary>What the Proof of delivery form asks of the Driver (P12, P6): who received, clean or with reservations, a PNG signature and 0 to 4 photos.</summary>
public sealed class ProofOfDeliveryRulesTests
{
    private static readonly ProofFileInfo Signature = new("image/png", 4_000);
    private static readonly ProofFileInfo Photo = new("image/jpeg", 2_000_000);

    private static ProofOfDeliveryForm Form(
        string? receivedBy = "J. Bakker", DeliveryCondition? condition = DeliveryCondition.Clean, string? remark = null, ProofFileInfo? signature = null, params ProofFileInfo[] photos) =>
        new(receivedBy, condition, remark, signature ?? Signature, photos);

    private static string[] Fields(ProofOfDeliveryForm form) => [.. ProofOfDeliveryRules.Check(form).Select(problem => problem.Field)];

    [Fact]
    public void ACleanDeliveryWithASignature_NeedsNothingElse()
    {
        ProofOfDeliveryRules.Check(Form()).Should().BeEmpty();
    }

    [Fact]
    public void ADeliveryWithFourPhotosAndReservations_IsWithinTheRules()
    {
        ProofOfDeliveryRules.Check(Form(condition: DeliveryCondition.Reservations, remark: "Two cartons crushed", photos: [Photo, Photo, Photo, Photo])).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WhoReceivedTheGoods_IsRequired(string? receivedBy) => Fields(Form(receivedBy: receivedBy)).Should().Equal("receivedBy");

    [Fact]
    public void ANameLongerThanTheTmsKeeps_IsNotTaken() => Fields(Form(receivedBy: new string('x', 201))).Should().Equal("receivedBy");

    [Fact]
    public void TheCondition_IsRequired() => Fields(Form(condition: null)).Should().Equal("condition");

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void ReservationsNeedARemark(string? remark) => Fields(Form(condition: DeliveryCondition.Reservations, remark: remark)).Should().Equal("remark");

    [Fact]
    public void ARemarkLongerThanTheTmsKeeps_IsNotTaken() => Fields(Form(condition: DeliveryCondition.Reservations, remark: new string('x', 501))).Should().Equal("remark");

    [Fact]
    public void TheSignature_IsRequired_AndNotEmpty()
    {
        ProofOfDeliveryRules.Check(new("J. Bakker", DeliveryCondition.Clean, null, null, [])).Select(p => p.Field).Should().Equal("signature");
        Fields(Form(signature: new ProofFileInfo("image/png", 0))).Should().Equal("signature");
    }

    [Theory]
    [InlineData("image/jpeg", 4_000)]
    [InlineData("image/png", 10_485_761)]
    public void TheSignature_IsAPngWithinTenMegabytes(string contentType, long size) => Fields(Form(signature: new ProofFileInfo(contentType, size))).Should().Equal("signature");

    [Fact]
    public void FivePhotos_AreOneTooMany() => Fields(Form(photos: [Photo, Photo, Photo, Photo, Photo])).Should().Equal("photos");

    [Theory]
    [InlineData("image/heic", 1_000)]
    [InlineData("application/pdf", 1_000)]
    [InlineData("image/jpeg", 10_485_761)]
    [InlineData("image/jpeg", 0)]
    public void APhoto_IsAJpegOrAPngWithinTenMegabytes(string contentType, long size) => Fields(Form(photos: [new ProofFileInfo(contentType, size)])).Should().Equal("photos");

    [Fact]
    public void EveryProblemIsReported_NotOnlyTheFirst() =>
        Fields(new ProofOfDeliveryForm(null, null, null, null, [])).Should().Equal("receivedBy", "condition", "signature");
}
