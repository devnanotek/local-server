# ==============================================================================
#  DEVNANOTEK —{{ENGINE_TITLE}} {{VERSION}} yapılandırması
#  BU DOSYA OTOMATİK ÜRETİLİR. Her "Uygula" / sürüm değişiminde yeniden yazılır.
#  Kendi eklemelerinizi şu dosyaya yazın (asla silinmez, [mysqld] altına eklenir):
#      {{CUSTOM_CNF}}
# ==============================================================================

[client]
port={{PORT}}
default-character-set=utf8mb4

[mysql]
default-character-set=utf8mb4

[mysqldump]
quick
max_allowed_packet=512M

[mysqld]
basedir="{{BASEDIR}}"
datadir="{{DATADIR}}"
tmpdir="{{TMPDIR}}"
port={{PORT}}
bind-address={{BIND}}

character-set-server=utf8mb4
collation-server={{COLLATION}}

max_connections=200
max_allowed_packet=512M
table_open_cache=2000
innodb_buffer_pool_size=256M
innodb_flush_log_at_trx_commit=1
innodb_file_per_table=1
secure-file-priv=""

log_error="{{LOGS}}/error.log"
general_log=0
slow_query_log=0
slow_query_log_file="{{LOGS}}/slow.log"

{{EXTRA}}

# ---- Kullanıcı ekleri ({{CUSTOM_CNF}}) ----------------------------------------
{{CUSTOM}}
