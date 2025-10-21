namespace Api.Contracts.Requests;
public sealed record CreateOrderRequest(Guid OrderId, decimal Amount);
