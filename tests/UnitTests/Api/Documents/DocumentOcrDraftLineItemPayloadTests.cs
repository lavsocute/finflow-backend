using System.Reflection;
using FinFlow.Api.GraphQL.Documents;
using FinFlow.Api.GraphQL.Payments;

namespace FinFlow.UnitTests.Api.Documents;

public sealed class DocumentOcrDraftLineItemPayloadTests
{
    [Fact]
    public void Payload_DeclaresEditableMonetaryFields_BeforeTotal()
    {
        var properties = typeof(DocumentOcrDraftLineItemPayload)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToList();

        Assert.Equal(
            [
                nameof(DocumentOcrDraftLineItemPayload.ItemName),
                nameof(DocumentOcrDraftLineItemPayload.Quantity),
                nameof(DocumentOcrDraftLineItemPayload.UnitPrice),
                nameof(DocumentOcrDraftLineItemPayload.DiscountPercent),
                nameof(DocumentOcrDraftLineItemPayload.DiscountAmount),
                nameof(DocumentOcrDraftLineItemPayload.TaxRate),
                nameof(DocumentOcrDraftLineItemPayload.TaxableAmount),
                nameof(DocumentOcrDraftLineItemPayload.TaxAmount),
                nameof(DocumentOcrDraftLineItemPayload.Total)
            ],
            properties);
    }

    [Fact]
    public void ApprovalPayload_DeclaresEditableMonetaryFields_BeforeTotal()
    {
        var properties = typeof(ApprovalLineItemPayload)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToList();

        Assert.Equal(
            [
                nameof(ApprovalLineItemPayload.Description),
                nameof(ApprovalLineItemPayload.Quantity),
                nameof(ApprovalLineItemPayload.UnitPrice),
                nameof(ApprovalLineItemPayload.DiscountPercent),
                nameof(ApprovalLineItemPayload.DiscountAmount),
                nameof(ApprovalLineItemPayload.TaxRate),
                nameof(ApprovalLineItemPayload.TaxableAmount),
                nameof(ApprovalLineItemPayload.TaxAmount),
                nameof(ApprovalLineItemPayload.Total)
            ],
            properties);
    }

    [Fact]
    public void SubmittedDocumentPayload_DeclaresEditableMonetaryFields_BeforeTotal()
    {
        var properties = typeof(MySubmittedDocumentLineItemPayload)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToList();

        Assert.Equal(
            [
                nameof(MySubmittedDocumentLineItemPayload.ItemName),
                nameof(MySubmittedDocumentLineItemPayload.Quantity),
                nameof(MySubmittedDocumentLineItemPayload.UnitPrice),
                nameof(MySubmittedDocumentLineItemPayload.DiscountPercent),
                nameof(MySubmittedDocumentLineItemPayload.DiscountAmount),
                nameof(MySubmittedDocumentLineItemPayload.TaxRate),
                nameof(MySubmittedDocumentLineItemPayload.TaxableAmount),
                nameof(MySubmittedDocumentLineItemPayload.TaxAmount),
                nameof(MySubmittedDocumentLineItemPayload.Total)
            ],
            properties);
    }

    [Fact]
    public void PaymentPayload_DeclaresEditableMonetaryFields_BeforeTotal()
    {
        var properties = typeof(PaymentDocumentLineItemPayload)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToList();

        Assert.Equal(
            [
                nameof(PaymentDocumentLineItemPayload.Description),
                nameof(PaymentDocumentLineItemPayload.Quantity),
                nameof(PaymentDocumentLineItemPayload.UnitPrice),
                nameof(PaymentDocumentLineItemPayload.DiscountPercent),
                nameof(PaymentDocumentLineItemPayload.DiscountAmount),
                nameof(PaymentDocumentLineItemPayload.TaxRate),
                nameof(PaymentDocumentLineItemPayload.TaxableAmount),
                nameof(PaymentDocumentLineItemPayload.TaxAmount),
                nameof(PaymentDocumentLineItemPayload.Total)
            ],
            properties);
    }
}
