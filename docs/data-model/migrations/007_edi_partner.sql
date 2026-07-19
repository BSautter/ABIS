-- 007_edi_partner.sql — the ABIS-owned EDI trading-partner profile backbone.
--
-- One row per (customer, transaction set) so each customer can have different requirements for their
-- 861 / 870 / 846 / 856 / 863 documents. The legacy system encoded this as a separate proc per customer
-- per document (edi_aleris_870, f_edi_novelis_861, p_846_cleveland_cliff_ccsc, …); the modern engine turns
-- the ENVELOPE (partner identity, separators, envelope version, GS functional code, file prefix) + the
-- enablement into data here, and uses `variant` to select the generator's body code path where a customer's
-- layout genuinely differs (e.g. 'novelis' vs 'aleris' for the 861). `item_reference` carries a per-partner
-- magic body constant (e.g. the Aleris 870 PRF*RV value). The receiving customer's own DUNS (N1*SU/N1*MF)
-- still comes from customer.customer_duns_number_string.
--
-- The app provisions this automatically at startup (AbisSchema.EnsureOwnedTablesAsync); this script is the
-- canonical DDL for manual/DBA use. Seed it from the legacy procs. GENERATION CONFIG ONLY — nothing transmits.
--
-- NOTE (Oracle): an empty component_separator / segment_suffix is stored as NULL; readers COALESCE to ''.

CREATE TABLE abis_edi_partner (
  customer_id          NUMBER(10)    NOT NULL,
  transaction_set      VARCHAR2(6)   NOT NULL,   -- '861','870','846','856','863'
  enabled              NUMBER(1)     DEFAULT 1 NOT NULL,
  variant              VARCHAR2(40),             -- selects the generator body path ('novelis','aleris',…)
  receiver_qualifier   VARCHAR2(4),              -- ISA/GS receiver qualifier
  receiver_id          VARCHAR2(20),             -- ISA08 + GS03 hub DUNS
  component_separator  VARCHAR2(2),              -- ISA16
  segment_suffix       VARCHAR2(2),              -- '' or '~'
  envelope_version     VARCHAR2(6),              -- ISA12
  gs_functional_code   VARCHAR2(4),              -- GS01
  gs_sender_code       VARCHAR2(20),             -- GS02 override when ≠ the ISA sender (e.g. Arconic R0P7ATN)
  gs_receiver_code     VARCHAR2(20),             -- GS03 override when ≠ the ISA receiver (e.g. Novelis 870)
  file_prefix          VARCHAR2(40),
  item_reference       VARCHAR2(40),             -- per-partner magic body constant
  updated_utc          DATE,
  updated_by           VARCHAR2(64),
  CONSTRAINT pk_abis_edi_partner PRIMARY KEY (customer_id, transaction_set));
-- Additive columns for tables provisioned before these existed (skip whichever already exists):
-- ALTER TABLE abis_edi_partner ADD (gs_sender_code VARCHAR2(20));
-- ALTER TABLE abis_edi_partner ADD (gs_receiver_code VARCHAR2(20));

-- Seed the partners visible in the legacy procs (adjust/extend from each partner's EDI implementation guide):
INSERT INTO abis_edi_partner (customer_id, transaction_set, enabled, variant, receiver_qualifier, receiver_id, component_separator, segment_suffix, envelope_version, gs_functional_code, gs_sender_code, file_prefix, item_reference) VALUES (1153, '861', 1, 'novelis', '09', '0015049350011G', '', '', '00200', 'RC', NULL, 'S_Novelis_', NULL);
INSERT INTO abis_edi_partner (customer_id, transaction_set, enabled, variant, receiver_qualifier, receiver_id, component_separator, segment_suffix, envelope_version, gs_functional_code, gs_sender_code, file_prefix, item_reference) VALUES (1459, '861', 1, 'novelis', '09', '0015049350011G', '', '', '00200', 'RC', NULL, 'S_Novelis_', NULL);
INSERT INTO abis_edi_partner (customer_id, transaction_set, enabled, variant, receiver_qualifier, receiver_id, component_separator, segment_suffix, envelope_version, gs_functional_code, gs_sender_code, file_prefix, item_reference) VALUES (2582, '861', 1, 'novelis', '09', '0015049350011G', '', '', '00200', 'RC', NULL, 'S_Novelis_', NULL);
INSERT INTO abis_edi_partner (customer_id, transaction_set, enabled, variant, receiver_qualifier, receiver_id, component_separator, segment_suffix, envelope_version, gs_functional_code, gs_sender_code, file_prefix, item_reference) VALUES (1980, '861', 1, 'aleris', 'ZZ', '964790856', '>', '', '00200', 'RC', NULL, 'S_edi_', NULL);
INSERT INTO abis_edi_partner (customer_id, transaction_set, enabled, variant, receiver_qualifier, receiver_id, component_separator, segment_suffix, envelope_version, gs_functional_code, gs_sender_code, file_prefix, item_reference) VALUES (1980, '870', 1, 'aleris', 'ZZ', '964790856', '>', '', '00401', 'RS', NULL, 'S_aleris_', '300578504');
-- Novelis 870 (Kingston 1153, Oswego 1459): per-job variant; GS03 receiver (001504935001) ≠ ISA08 (0015049350011G).
INSERT INTO abis_edi_partner (customer_id, transaction_set, enabled, variant, receiver_qualifier, receiver_id, component_separator, segment_suffix, envelope_version, gs_functional_code, gs_receiver_code, file_prefix, item_reference) VALUES (1153, '870', 1, 'novelis', '09', '0015049350011G', '', '', '00401', 'RS', '001504935001', 'S_novelis_870_', NULL);
INSERT INTO abis_edi_partner (customer_id, transaction_set, enabled, variant, receiver_qualifier, receiver_id, component_separator, segment_suffix, envelope_version, gs_functional_code, gs_receiver_code, file_prefix, item_reference) VALUES (1459, '870', 1, 'novelis', '09', '0015049350011G', '', '', '00401', 'RS', '001504935001', 'S_novelis_870_', NULL);
INSERT INTO abis_edi_partner (customer_id, transaction_set, enabled, variant, receiver_qualifier, receiver_id, component_separator, segment_suffix, envelope_version, gs_functional_code, gs_sender_code, file_prefix, item_reference) VALUES (2784, '861', 1, 'arconic', '01', '961613887', '>', '', '00401', 'SH', 'R0P7ATN', 'S_arconic_861_', NULL);
INSERT INTO abis_edi_partner (customer_id, transaction_set, enabled, variant, receiver_qualifier, receiver_id, component_separator, segment_suffix, envelope_version, gs_functional_code, gs_sender_code, file_prefix, item_reference) VALUES (2776, '861', 1, 'constellium', '01', '043207177', '@', '', '00401', 'SH', NULL, 'S_constellium_861_', NULL);
COMMIT;
