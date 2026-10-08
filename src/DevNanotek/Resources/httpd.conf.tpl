# ==============================================================================
#  DEVNANOTEK — Apache HTTP Server yapılandırması
#  BU DOSYA OTOMATİK ÜRETİLİR. Her "Uygula" / sürüm değişiminde yeniden yazılır.
#  Kendi eklemelerinizi şu dosyaya yazın (asla silinmez):
#      {{CUSTOM_CONF}}
#  Sanal hostlar:  {{VHOSTS_DIR}}\*.conf
# ==============================================================================

Define SRVROOT "{{SRVROOT}}"
ServerRoot "${SRVROOT}"

# Varsayılan: yalnızca bu bilgisayar (127.0.0.1 ve ::1). Ayarlar > "Yerel ağdan erişim" açıksa tüm ağ arayüzleri.
{{LISTEN_HTTP}}

# ---- Modüller -----------------------------------------------------------------
LoadModule access_compat_module modules/mod_access_compat.so
LoadModule actions_module modules/mod_actions.so
LoadModule alias_module modules/mod_alias.so
LoadModule allowmethods_module modules/mod_allowmethods.so
LoadModule asis_module modules/mod_asis.so
LoadModule auth_basic_module modules/mod_auth_basic.so
LoadModule authn_core_module modules/mod_authn_core.so
LoadModule authn_file_module modules/mod_authn_file.so
LoadModule authz_core_module modules/mod_authz_core.so
LoadModule authz_groupfile_module modules/mod_authz_groupfile.so
LoadModule authz_host_module modules/mod_authz_host.so
LoadModule authz_user_module modules/mod_authz_user.so
LoadModule autoindex_module modules/mod_autoindex.so
LoadModule cgi_module modules/mod_cgi.so
LoadModule deflate_module modules/mod_deflate.so
LoadModule dir_module modules/mod_dir.so
LoadModule env_module modules/mod_env.so
LoadModule expires_module modules/mod_expires.so
LoadModule filter_module modules/mod_filter.so
LoadModule headers_module modules/mod_headers.so
LoadModule include_module modules/mod_include.so
LoadModule info_module modules/mod_info.so
LoadModule log_config_module modules/mod_log_config.so
LoadModule mime_module modules/mod_mime.so
LoadModule negotiation_module modules/mod_negotiation.so
LoadModule proxy_module modules/mod_proxy.so
LoadModule proxy_http_module modules/mod_proxy_http.so
LoadModule proxy_fcgi_module modules/mod_proxy_fcgi.so
LoadModule proxy_wstunnel_module modules/mod_proxy_wstunnel.so
LoadModule rewrite_module modules/mod_rewrite.so
LoadModule setenvif_module modules/mod_setenvif.so
LoadModule socache_shmcb_module modules/mod_socache_shmcb.so
LoadModule ssl_module modules/mod_ssl.so
LoadModule status_module modules/mod_status.so
LoadModule vhost_alias_module modules/mod_vhost_alias.so

{{PHP_BLOCK}}

# ---- Genel --------------------------------------------------------------------
ServerAdmin admin@localhost
ServerName localhost:{{HTTP_PORT}}
ServerTokens Prod
ServerSignature Off
UseCanonicalName Off
HostnameLookups Off
Timeout 300
KeepAlive On
MaxKeepAliveRequests 100
KeepAliveTimeout 5
EnableMMAP off
EnableSendfile off
AcceptFilter http none
AcceptFilter https none

<IfModule mpm_winnt_module>
    ThreadsPerChild        150
    MaxConnectionsPerChild 0
    ThreadStackSize        8388608
</IfModule>

<Directory />
    AllowOverride none
    Require all denied
</Directory>

# ---- Belge kökü (localhost) ---------------------------------------------------
DocumentRoot "{{DOCROOT}}"
<Directory "{{DOCROOT}}">
    Options Indexes FollowSymLinks Includes ExecCGI
    AllowOverride All
    Require all granted
</Directory>

<IfModule dir_module>
    DirectoryIndex index.php index.html index.htm default.php default.html
</IfModule>

<Files ".ht*">
    Require all denied
</Files>

# Uygulamaların okuyabilmesi için DEVNANOTEK bilgileri
SetEnv DEVNANOTEK_ROOT "{{ROOT}}"
SetEnv DEVNANOTEK_DB_PORT "{{DB_PORT}}"
SetEnv DEVNANOTEK_PG_PORT "{{PG_PORT}}"
SetEnv DEVNANOTEK_MAILPIT_PORT "{{MAILPIT_PORT}}"
SetEnv DEVNANOTEK_SMTP_PORT "{{SMTP_PORT}}"
SetEnv DEVNANOTEK_PHP "{{PHP_VERSION}}"

# ---- Günlükler ----------------------------------------------------------------
ErrorLog "{{LOGS}}/error.log"
LogLevel warn
<IfModule log_config_module>
    LogFormat "%h %l %u %t \"%r\" %>s %b \"%{Referer}i\" \"%{User-Agent}i\"" combined
    LogFormat "%h %l %u %t \"%r\" %>s %b" common
    CustomLog "{{LOGS}}/access.log" common
</IfModule>
PidFile "{{LOGS}}/httpd.pid"

<IfModule alias_module>
    ScriptAlias /cgi-bin/ "${SRVROOT}/cgi-bin/"
</IfModule>
<Directory "${SRVROOT}/cgi-bin">
    AllowOverride None
    Options None
    Require all granted
</Directory>

<IfModule mime_module>
    TypesConfig conf/mime.types
    AddType application/x-compress .Z
    AddType application/x-gzip .gz .tgz
    AddType text/html .shtml
    AddOutputFilter INCLUDES .shtml
</IfModule>

<IfModule mime_magic_module>
    MIMEMagicFile conf/magic
</IfModule>

<IfModule deflate_module>
    AddOutputFilterByType DEFLATE text/html text/plain text/xml text/css text/javascript application/javascript application/json application/xml image/svg+xml
</IfModule>

<IfModule headers_module>
    RequestHeader unset Proxy early
</IfModule>

<IfModule status_module>
    <Location "/server-status">
        SetHandler server-status
        Require local
    </Location>
</IfModule>

# ---- phpMyAdmin (http://localhost/phpmyadmin) ve Adminer (http://localhost/adminer)
{{PMA_BLOCK}}

# ---- SSL genel ayarları -------------------------------------------------------
{{SSL_BLOCK}}

# ---- Sanal hostlar (00-localhost.conf her zaman ilk gelir) --------------------
IncludeOptional "{{VHOSTS_DIR}}/*.conf"

# ---- Kullanıcı ekleri (bu dosya hiç bir zaman üzerine yazılmaz) ---------------
Include "{{CUSTOM_CONF}}"
