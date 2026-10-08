# ==============================================================================
#  DEVNANOTEK — Nginx yapılandırması
#  BU DOSYA OTOMATİK ÜRETİLİR. Her "Uygula" / sürüm değişiminde yeniden yazılır.
#  Kendi eklemelerinizi şu dosyaya yazın (asla silinmez; http {} bloğu içindedir):
#      {{CUSTOM_CONF}}
#  Sanal hostlar:  {{VHOSTS_DIR}}\*.conf
# ==============================================================================

worker_processes  1;
error_log  "{{LOGS}}/error.log" warn;
pid        "{{LOGS}}/nginx.pid";

events {
    worker_connections  1024;
}

http {
    include       "{{NGINX_DIR}}/conf/mime.types";
    default_type  application/octet-stream;
    charset       utf-8;

    log_format  main  '$remote_addr - $remote_user [$time_local] "$request" '
                      '$status $body_bytes_sent "$http_referer" "$http_user_agent"';
    access_log  "{{LOGS}}/access.log"  main;

    sendfile            off;
    tcp_nopush          on;
    keepalive_timeout   65;
    client_max_body_size 1024m;
    server_names_hash_bucket_size 128;
    server_tokens       off;

    gzip  on;
    gzip_types text/plain text/css application/json application/javascript text/xml application/xml image/svg+xml;

    fastcgi_read_timeout 600;
    fastcgi_buffers      16 16k;
    fastcgi_buffer_size  32k;

    # PHP FastCGI süreçleri (DevNanotek-PHP-FCGI servisi tarafından çalıştırılır)
    upstream devnanotek_php {
{{FCGI_SERVERS}}
    }

    include "{{VHOSTS_DIR}}/*.conf";
    include "{{CUSTOM_CONF}}";
}
