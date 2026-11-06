-- Função em PL/pgSQL e procedure em SQL: o ponto e vírgula dentro do $$ não separa instruções.
CREATE OR REPLACE FUNCTION baixar_estoque(p_pedido_id integer, OUT linhas integer)
RETURNS integer
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE produtos p
       SET estoque = p.estoque - i.quantidade
      FROM itens_pedido i
     WHERE i.produto_id = p.id
       AND i.pedido_id = p_pedido_id;

    GET DIAGNOSTICS linhas = ROW_COUNT;

    INSERT INTO log_eventos (ocorrido_em, mensagem) VALUES (now(), 'estoque baixado; ok');
END;
$$;

CREATE PROCEDURE arquivar_pedidos(IN p_antes timestamptz)
LANGUAGE sql
AS $$
    DELETE FROM pedidos WHERE criado_em < p_antes AND status = 'cancelado';
$$;
