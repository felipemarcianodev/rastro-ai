-- Tabela sem chave primária (heap), de propósito.
CREATE TABLE dbo.log_eventos (
    ocorrido_em DATETIME2 NOT NULL,
    mensagem NVARCHAR(400) NULL
);
