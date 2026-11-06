-- Loja fictícia de vários inquilinos (PostgreSQL): toda tabela leva tenant_id.
CREATE TABLE categorias (
    id uuid NOT NULL PRIMARY KEY,
    tenant_id uuid NOT NULL,
    nome text NOT NULL
);

CREATE TABLE itens (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    loja_id uuid NOT NULL,
    categoria_id uuid NULL,
    item_pai_id uuid NULL,
    titulo text NOT NULL,
    status text NOT NULL,
    rotulos text[] NULL,
    dados jsonb NULL,
    criado_em timestamptz NOT NULL,
    CONSTRAINT pk_itens PRIMARY KEY (id),
    CONSTRAINT fk_itens_categorias_categoria_id FOREIGN KEY (categoria_id) REFERENCES categorias (id),
    CONSTRAINT fk_itens_itens_item_pai_id FOREIGN KEY (item_pai_id) REFERENCES itens (id)
);

CREATE INDEX ix_itens_tenant_loja ON itens (tenant_id, loja_id);
CREATE INDEX ix_itens_item_pai_id ON itens (item_pai_id);
CREATE INDEX ix_itens_titulo_trgm ON itens USING gin (titulo gin_trgm_ops);
CREATE INDEX ix_itens_status_hash ON itens USING hash (status);
