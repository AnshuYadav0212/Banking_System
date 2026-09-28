namespace Banking_System.ApiService.DTOs;

/// <summary>
/// One row of the staff "Customer accounts" list. Identifiers are masked before
/// they leave the API (see <c>SensitiveMasking</c>): the full CIF, phone number
/// and email address are never sent to the browser.
/// </summary>
public sealed record CustomerAccountRow(
    string FullName,
    string MaskedCif,
    string MaskedPhone,
    string? MaskedEmail,
    int AccountCount,
    decimal TotalBalance,
    bool HasOnlineBanking,
    string Status);

public sealed record CustomerAccountPage(
    IReadOnlyList<CustomerAccountRow> Items,
    int TotalCount,
    int Page,
    int PageSize);
