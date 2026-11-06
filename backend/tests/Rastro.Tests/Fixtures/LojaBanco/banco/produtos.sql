-- Catálogo de produtos: vem do projeto de banco, não do EF.
CREATE TABLE [dbo].[produtos] (
    [id] INT IDENTITY (1, 1) NOT NULL,
    [nome] NVARCHAR (160) NOT NULL,
    [preco] DECIMAL (10, 2) NOT NULL,
    [estoque] INT NOT NULL CONSTRAINT [DF_produtos_estoque] DEFAULT ((0)),
    CONSTRAINT [PK_produtos] PRIMARY KEY CLUSTERED ([id] ASC)
);
GO

CREATE INDEX [IX_produtos_nome] ON [dbo].[produtos] ([nome]);
GO

ALTER TABLE [dbo].[itens_pedido]
    ADD CONSTRAINT [FK_itens_pedido_produtos_produto_id] FOREIGN KEY ([produto_id]) REFERENCES [dbo].[produtos] ([id]);
GO
