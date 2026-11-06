CREATE VIEW dbo.vw_pedidos_por_cliente
AS
SELECT c.id AS cliente_id, c.nome, COUNT(p.id) AS pedidos
FROM dbo.clientes c
LEFT JOIN dbo.pedidos p ON p.cliente_id = c.id
GROUP BY c.id, c.nome;
GO
