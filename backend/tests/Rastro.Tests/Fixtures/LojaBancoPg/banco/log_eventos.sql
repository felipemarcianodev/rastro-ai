-- Tabela sem chave primária, de propósito.
CREATE TABLE log_eventos (
    ocorrido_em timestamptz NOT NULL,
    mensagem varchar(400)
);
