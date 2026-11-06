CREATE VIEW vw_pedidos_por_cliente AS
SELECT c.id AS cliente_id, c.nome, count(p.id) AS pedidos
FROM clientes c
LEFT JOIN pedidos p ON p.cliente_id = c.id
GROUP BY c.id, c.nome;

CREATE MATERIALIZED VIEW mv_vendas_por_dia AS
SELECT criado_em::date AS dia, sum(total) AS total
FROM pedidos
GROUP BY 1;
