-- Catálogo de produtos: vem do script de banco, não do EF.
SET search_path = public;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE public.produtos (
    id serial PRIMARY KEY,
    nome varchar(160) NOT NULL,
    preco numeric(10, 2) NOT NULL,
    estoque int NOT NULL DEFAULT 0,
    atributos jsonb,
    codigo uuid NOT NULL DEFAULT gen_random_uuid(),
    criado_em timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_produtos_nome ON public.produtos USING btree (nome);

ALTER TABLE itens_pedido
    ADD CONSTRAINT fk_itens_pedido_produtos_produto_id FOREIGN KEY (produto_id) REFERENCES produtos (id);
