CREATE PROCEDURE dbo.sp_baixar_estoque
    @pedido_id INT,
    @linhas INT OUTPUT
AS
BEGIN
    UPDATE p
       SET p.estoque = p.estoque - i.quantidade
      FROM dbo.produtos p
      JOIN dbo.itens_pedido i ON i.produto_id = p.id
     WHERE i.pedido_id = @pedido_id;

    SET @linhas = @@ROWCOUNT;

    INSERT INTO dbo.log_eventos (ocorrido_em, mensagem) VALUES (SYSUTCDATETIME(), N'estoque baixado');
END
GO
