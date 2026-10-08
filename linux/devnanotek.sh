#!/usr/bin/env bash
# =============================================================================
#  DEVNANOTEK Local Server — Linux sürümü                    https://devnanotek.net
#
#  PHP (çoklu sürüm) · Apache / Nginx · MariaDB / MySQL · PostgreSQL · phpMyAdmin
#  Adminer · Mailpit (test e-posta) · Node.js · Composer · mkcert (yerel HTTPS)
#
#  Desteklenen dağıtımlar (64 bit, x86_64 ve ARM64):
#    Ubuntu 22.04 / 24.04 / 26.04 · Debian 11 / 12 / 13 · Linux Mint 21-22 · Pop!_OS · Zorin
#    AlmaLinux · Rocky · RHEL · CentOS Stream · Oracle Linux · CloudLinux (8, 9, 10) · Fedora
#
#  İlk kurulum :  sudo bash devnanotek.sh install
#  Sonrası     :  devnanotek            (menü)
#                 devnanotek help       (tüm komutlar)
# =============================================================================
set -o pipefail

DN_VERSION="1.3.0"
DN_ROOT="${DN_ROOT:-/opt/devnanotek}"
DN_DOCS="$DN_ROOT/httpdocs"
DN_ETC="$DN_ROOT/etc"
DN_STATE="$DN_ETC/devnanotek.conf"
DN_LOG_DIR="$DN_ROOT/logs"
DN_LOG="$DN_LOG_DIR/devnanotek.log"
DN_BACKUPS="$DN_ROOT/backups"
DN_SSL="$DN_ROOT/ssl"
DN_TMP="$DN_ROOT/tmp"
DN_LINK="/usr/local/bin/devnanotek"
HOSTS_BEGIN="# >>> DEVNANOTEK BEGIN (bu blok otomatik yönetilir)"
HOSTS_END="# <<< DEVNANOTEK END"
PHP_CANDIDATES="8.5 8.4 8.3 8.2 8.1 8.0 7.4"
PHP_DEFAULT="8.4"
NODE_DEFAULT="24"
PG_DEFAULT="17"

# ---------------------------------------------------------------- görünüm --
if [ -t 1 ]; then
  C_G=$'\e[32m'; C_Y=$'\e[33m'; C_R=$'\e[31m'; C_B=$'\e[1m'; C_D=$'\e[2m'; C_O=$'\e[38;5;214m'; C_0=$'\e[0m'
else
  C_G=; C_Y=; C_R=; C_B=; C_D=; C_O=; C_0=
fi
log()  { [ -d "$DN_LOG_DIR" ] && printf '%s %s\n' "$(date '+%F %T')" "$*" >> "$DN_LOG" 2>/dev/null; return 0; }
ok()   { printf '%s✔%s %s\n' "$C_G" "$C_0" "$*"; log "OK $*"; }
warn() { printf '%s⚠%s %s\n' "$C_Y" "$C_0" "$*"; log "UYARI $*"; }
err()  { printf '%s✖%s %s\n' "$C_R" "$C_0" "$*" >&2; log "HATA $*"; }
info() { printf '%s•%s %s\n' "$C_D" "$C_0" "$*"; log "$*"; }
die()  { err "$*"; exit 1; }
# komutu çalıştırır, çıktısını günlüğe yazar; başarısızsa hata gösterir
run()  { log "\$ $*"; "$@" >>"$DN_LOG" 2>&1 && return 0; err "Komut başarısız: $*  (ayrıntı: $DN_LOG)"; return 1; }
quiet(){ "$@" >>"$DN_LOG" 2>&1; }

banner() {
  printf '\n  %s%sDEV%s%sNANOTEK%s  %sLocal Server %s · Linux%s\n\n' "$C_B" "$C_O" "$C_0" "$C_B" "$C_0" "$C_D" "$DN_VERSION" "$C_0"
}

need_root() { [ "$(id -u)" -eq 0 ] || die "Bu işlem yönetici izni ister. Şöyle çalıştırın: sudo devnanotek $*"; }

confirm() { # confirm "Soru?" [e|h]  → 0 = evet
  local def="${2:-e}" a
  [ -n "$DN_YES" ] && return 0
  if [ "$def" = e ]; then read -r -p "$1 [E/h] " a; [ -z "$a" ] || [[ "$a" =~ ^[eEyY] ]]
  else read -r -p "$1 [e/H] " a; [[ "$a" =~ ^[eEyY] ]]; fi
}

has() { command -v "$1" >/dev/null 2>&1; }

# ------------------------------------------------------------ durum dosyası --
DN_USER=""; WEB="apache"; PHP=""; DB="mariadb"; DB_VER=""; DB_MANAGED=0; PG=""; NODE=""; MAILPIT=1; SSL=0; MODE="always"; LAN=0
PMA=1; ADMINER=1; COMPOSER=1; HTTP_PORT=80; HTTPS_PORT=443; DB_PORT=3306; PG_PORT=5432; SMTP_PORT=1025; MAILPIT_UI_PORT=8025
DEFAULT_SITE_DISABLED=0; SELINUX_DONE=0

load_state() { [ -f "$DN_STATE" ] && . "$DN_STATE"; return 0; }
save_state() {
  mkdir -p "$DN_ETC"
  {
    echo "# DEVNANOTEK durum dosyası — 'devnanotek' komutlarıyla değiştirin"
    local k
    for k in DN_USER WEB PHP DB DB_VER DB_MANAGED PG NODE MAILPIT SSL MODE LAN PMA ADMINER COMPOSER \
             HTTP_PORT HTTPS_PORT DB_PORT PG_PORT SMTP_PORT MAILPIT_UI_PORT DEFAULT_SITE_DISABLED SELINUX_DONE; do
      printf '%s=%q\n' "$k" "${!k}"
    done
  } > "$DN_STATE"
}
record() { mkdir -p "$DN_ETC"; grep -qxF "$2" "$DN_ETC/$1" 2>/dev/null || echo "$2" >> "$DN_ETC/$1"; }

# ----------------------------------------------------------- sistem algıla --
detect_os() {
  [ -r /etc/os-release ] || die "/etc/os-release bulunamadı: bu sistem desteklenmiyor."
  # shellcheck disable=SC1091
  . /etc/os-release
  OS_ID="${ID:-}"; OS_LIKE="${ID_LIKE:-}"; OS_VER="${VERSION_ID:-}"; OS_NAME="${PRETTY_NAME:-$ID}"
  OS_CODENAME="${VERSION_CODENAME:-}"; UBU_CODENAME="${UBUNTU_CODENAME:-}"
  ARCH="$(uname -m)"
  FAMILY=unknown
  case " $OS_ID $OS_LIKE " in
    *" ubuntu "*|*" debian "*) FAMILY=deb ;;
    *" rhel "*|*" fedora "*|*" centos "*) FAMILY=rpm ;;
  esac
  IS_UBUNTU=0; { [ "$OS_ID" = ubuntu ] || [ -n "$UBU_CODENAME" ]; } && IS_UBUNTU=1
  DEB_CODE=""
  if [ "$FAMILY" = deb ]; then
    if [ "$IS_UBUNTU" = 1 ]; then DEB_CODE="${UBU_CODENAME:-$OS_CODENAME}"
    else case "$OS_CODENAME" in bullseye|bookworm|trixie|forky) DEB_CODE="$OS_CODENAME" ;; esac; fi
  fi
  RPM_KIND=""; EL_VER=""; FEDORA_VER=""
  if [ "$FAMILY" = rpm ]; then
    if [ "$OS_ID" = fedora ]; then RPM_KIND=fedora; FEDORA_VER="$OS_VER"; else RPM_KIND=el; EL_VER="${OS_VER%%.*}"; fi
  fi
  HAS_SYSTEMD=0; [ -d /run/systemd/system ] && HAS_SYSTEMD=1
  case "$ARCH" in x86_64|aarch64|arm64) ;; *) warn "İşlemci mimarisi $ARCH: bazı paketler (Node.js, mkcert, PostgreSQL) bulunamayabilir." ;; esac

  if [ "$FAMILY" = deb ]; then
    APACHE_SVC=apache2; APACHE_USER=www-data; APACHE_LOG=/var/log/apache2
    APACHE_CONF=/etc/apache2/sites-available/devnanotek.conf
    NGINX_USER=www-data
  else
    APACHE_SVC=httpd; APACHE_USER=apache; APACHE_LOG=/var/log/httpd
    APACHE_CONF=/etc/httpd/conf.d/devnanotek.conf
    NGINX_USER=nginx
  fi
  NGINX_CONF=/etc/nginx/conf.d/devnanotek.conf
}

require_supported() {
  [ "$FAMILY" != unknown ] || die "Bu dağıtım ($OS_NAME) henüz desteklenmiyor. Desteklenenler: Ubuntu, Debian, Linux Mint, Pop!_OS, AlmaLinux, Rocky, RHEL, CentOS Stream, Oracle Linux, Fedora."
  if [ "$FAMILY" = rpm ] && [ "$RPM_KIND" = el ] && [ "${EL_VER:-0}" -lt 8 ]; then die "Enterprise Linux $EL_VER çok eski (8 veya üstü gerekir)."; fi
}

web_user() { [ "$WEB" = nginx ] && echo "$NGINX_USER" || echo "$APACHE_USER"; }
web_svc()  { [ "$WEB" = nginx ] && echo nginx || echo "$APACHE_SVC"; }

# -------------------------------------------------------------- servisler --
svc() { # svc start|stop|restart|enable|disable|is-active|is-failed <servis>
  local a="$1" s="$2"
  if [ "$HAS_SYSTEMD" = 1 ]; then
    case "$a" in
      is-active|is-failed|is-enabled) systemctl "$a" --quiet "$s" 2>/dev/null ;;
      enable|disable) quiet systemctl "$a" "$s" ;;
      *) quiet systemctl "$a" "$s" ;;
    esac
  else
    case "$a" in
      enable|disable|is-enabled) return 0 ;;
      is-failed) return 1 ;;
      is-active) service "$s" status >/dev/null 2>&1 ;;
      *) quiet service "$s" "$a" ;;
    esac
  fi
}

# --------------------------------------------------------------- paketler --
pkg_update() { [ "$FAMILY" = deb ] && run env DEBIAN_FRONTEND=noninteractive apt-get update -y; return 0; }
pkg_installed() {
  if [ "$FAMILY" = deb ]; then dpkg-query -W -f='${Status}' "$1" 2>/dev/null | grep -q "install ok installed"
  else rpm -q "$1" >/dev/null 2>&1; fi
}
pkg_available() {
  if [ "$FAMILY" = deb ]; then [ -n "$(apt-cache policy "$1" 2>/dev/null | awk '/Candidate:/ {print $2}' | grep -v '(none)')" ]
  else dnf -q list --available "$1" >/dev/null 2>&1 || pkg_installed "$1"; fi
}
pkg_install() { # yalnız yeni kurulanlar kaydedilir; kaldırırken yalnız bunlar silinir
  local new=() p
  for p in "$@"; do pkg_installed "$p" || new+=("$p"); done
  [ ${#new[@]} -eq 0 ] && return 0
  info "Kuruluyor: ${new[*]}"
  if [ "$FAMILY" = deb ]; then
    run env DEBIAN_FRONTEND=noninteractive apt-get install -y -o Dpkg::Options::=--force-confdef -o Dpkg::Options::=--force-confold "${new[@]}" || return 1
  else
    run dnf install -y "${new[@]}" || return 1
  fi
  for p in "${new[@]}"; do record installed-packages.txt "$p"; done
}
pkg_install_available() { # depoda olmayan isteğe bağlı paketleri atlar
  local ok=() p
  for p in "$@"; do if pkg_installed "$p" || pkg_available "$p"; then ok+=("$p"); else log "yok (atlandı): $p"; fi; done
  [ ${#ok[@]} -gt 0 ] && pkg_install "${ok[@]}"
}

base_tools() { # yalnız eksik araçlar kurulur (EL9'daki curl-minimal ile çakışmasın diye)
  local need=()
  if [ "$FAMILY" = deb ]; then
    pkg_update
    pkg_install ca-certificates gnupg || return 1
    has curl || need+=(curl); has xz || need+=(xz-utils); has whiptail || need+=(whiptail)
  else
    pkg_installed ca-certificates || need+=(ca-certificates)
    has curl || need+=(curl); has xz || need+=(xz); has whiptail || need+=(newt)
    quiet dnf config-manager --help || need+=(dnf-plugins-core)
  fi
  has tar || need+=(tar); has unzip || need+=(unzip)
  [ ${#need[@]} -eq 0 ] || pkg_install "${need[@]}"
}

# ---------------------------------------------------------- PHP deposu ----
setup_php_repo() {
  [ -f "$DN_ETC/.php-repo" ] && return 0
  local repo=distro
  if [ "$FAMILY" = deb ]; then
    if [ "$IS_UBUNTU" = 1 ]; then
      pkg_install software-properties-common
      info "PHP deposu ekleniyor (ppa:ondrej/php — tüm PHP sürümleri)..."
      if run add-apt-repository -y ppa:ondrej/php; then repo=ondrej; record added-repos.txt "ppa:ondrej/php"; fi
    elif [ -n "$DEB_CODE" ]; then
      info "PHP deposu ekleniyor (packages.sury.org — tüm PHP sürümleri)..."
      local had_key=0; pkg_installed debsuryorg-archive-keyring && had_key=1
      if run curl -fsSL -o /tmp/debsuryorg-archive-keyring.deb https://packages.sury.org/debsuryorg-archive-keyring.deb \
         && run dpkg -i /tmp/debsuryorg-archive-keyring.deb; then
        echo "deb [signed-by=/usr/share/keyrings/debsuryorg-archive-keyring.gpg] https://packages.sury.org/php/ $DEB_CODE main" > /etc/apt/sources.list.d/devnanotek-php.list
        record added-repos.txt /etc/apt/sources.list.d/devnanotek-php.list
        [ "$had_key" = 0 ] && record installed-packages.txt debsuryorg-archive-keyring
        repo=sury
      fi
    fi
    [ "$repo" = distro ] && warn "Çoklu PHP deposu eklenemedi; yalnız dağıtımın kendi PHP sürümü kullanılabilir."
    pkg_update
  else
    info "PHP deposu ekleniyor (Remi — tüm PHP sürümleri)..."
    if [ "$RPM_KIND" = el ]; then
      if ! pkg_installed epel-release; then
        run dnf install -y "https://dl.fedoraproject.org/pub/epel/epel-release-latest-${EL_VER}.noarch.rpm" || return 1
        record installed-packages.txt epel-release
      fi
      # CRB / PowerTools (bazı PHP eklentilerinin bağımlılıkları) — dnf4 ve dnf5 sözdizimi
      quiet dnf config-manager --set-enabled crb || quiet dnf config-manager setopt crb.enabled=1 \
        || quiet dnf config-manager --set-enabled powertools || quiet dnf config-manager --set-enabled "ol${EL_VER}_codeready_builder" || true
      if ! pkg_installed remi-release; then
        run dnf install -y "https://rpms.remirepo.net/enterprise/remi-release-${EL_VER}.rpm" || return 1
        record installed-packages.txt remi-release
      fi
    elif ! pkg_installed remi-release; then
      run dnf install -y "https://rpms.remirepo.net/fedora/remi-release-${FEDORA_VER}.rpm" || return 1
      record installed-packages.txt remi-release
    fi
    repo=remi
  fi
  echo "$repo" > "$DN_ETC/.php-repo"
}

# ------------------------------------------------------- PHP yardımcıları --
nodot() { echo "${1//./}"; }
php_pkg() { # php_pkg 8.4 gd  →  php8.4-gd | php84-php-gd
  local v="$1" e="$2"
  if [ "$FAMILY" = deb ]; then echo "php$v-$e"; return; fi
  case "$e" in zip) e=pecl-zip ;; mysql) e=mysqlnd ;; sqlite3) e=pdo ;; esac
  echo "php$(nodot "$v")-php-$e"
}
php_fpm_svc()  { [ "$FAMILY" = deb ] && echo "php$1-fpm" || echo "php$(nodot "$1")-php-fpm"; }
php_pool_dir() { [ "$FAMILY" = deb ] && echo "/etc/php/$1/fpm/pool.d" || echo "/etc/opt/remi/php$(nodot "$1")/php-fpm.d"; }
php_sock()     { [ "$FAMILY" = deb ] && echo "/run/php/devnanotek-php$1.sock" || echo "/var/opt/remi/php$(nodot "$1")/run/php-fpm/devnanotek.sock"; }
php_cli()      { [ "$FAMILY" = deb ] && echo "/usr/bin/php$1" || echo "/opt/remi/php$(nodot "$1")/root/usr/bin/php"; }
php_ini_files() {
  if [ "$FAMILY" = deb ]; then echo "/etc/php/$1/fpm/conf.d/99-devnanotek.ini /etc/php/$1/cli/conf.d/99-devnanotek.ini"
  else echo "/etc/opt/remi/php$(nodot "$1")/php.d/99-devnanotek.ini"; fi
}
php_installed() { [ -x "$(php_cli "$1")" ] && pkg_installed "$(php_pkg "$1" fpm)"; }
php_installed_list() { local v; for v in $PHP_CANDIDATES; do php_installed "$v" && printf '%s ' "$v"; done; }
php_available_list() { local v; for v in $PHP_CANDIDATES; do { php_installed "$v" || pkg_available "$(php_pkg "$v" fpm)"; } && printf '%s ' "$v"; done; }

PHP_EXT_PKGS="cli common fpm mysql pgsql sqlite3 gd zip intl mbstring curl xml bcmath soap opcache gmp readline sodium"

install_php() {
  local v="$1" pk=() e
  setup_php_repo || return 1
  if ! php_installed "$v" && ! pkg_available "$(php_pkg "$v" fpm)"; then
    err "PHP $v bu dağıtımın depolarında yok. Kullanılabilir: $(php_available_list)"; return 1
  fi
  for e in $PHP_EXT_PKGS; do pk+=("$(php_pkg "$v" "$e")"); done
  pkg_install_available "${pk[@]}" || return 1
  php_installed "$v" || { err "PHP $v kurulamadı."; return 1; }
  write_php_pool "$v"; write_php_ini "$v"
  svc enable "$(php_fpm_svc "$v")"; svc restart "$(php_fpm_svc "$v")"
  ok "PHP $v hazır."
}

write_php_pool() {
  local v="$1" dir sock group
  dir="$(php_pool_dir "$v")"; sock="$(php_sock "$v")"; group="$(id -gn "$DN_USER" 2>/dev/null || echo "$DN_USER")"
  mkdir -p "$dir" "$DN_TMP/sessions" "$DN_TMP/upload"
  chown -R "$DN_USER:$group" "$DN_TMP"
  touch "$DN_LOG_DIR/php-error.log"; chown "$DN_USER:$group" "$DN_LOG_DIR/php-error.log"
  cat > "$dir/devnanotek.conf" <<EOF
; DEVNANOTEK — PHP $v havuzu. BU DOSYA OTOMATİK ÜRETİLİR (devnanotek apply).
; PHP, projelerinizin sahibi olan kullanıcıyla ($DN_USER) çalışır: yüklenen dosyalar size ait olur.
[devnanotek]
user = $DN_USER
group = $group
listen = $sock
listen.owner = $(web_user)
listen.group = $(web_user)
listen.mode = 0660
pm = ondemand
pm.max_children = 10
pm.process_idle_timeout = 30s
catch_workers_output = yes
php_admin_value[error_log] = $DN_LOG_DIR/php-error.log
php_admin_value[session.save_path] = $DN_TMP/sessions
php_admin_value[upload_tmp_dir] = $DN_TMP/upload
env[PATH] = /usr/local/bin:/usr/bin:/bin
env[DEVNANOTEK_ROOT] = $DN_ROOT
env[DEVNANOTEK_DB_PORT] = $DB_PORT
env[DEVNANOTEK_PG_PORT] = $PG_PORT
env[DEVNANOTEK_SMTP_PORT] = $SMTP_PORT
env[DEVNANOTEK_MAILPIT_PORT] = $MAILPIT_UI_PORT
EOF
}

write_php_ini() {
  local v="$1" f
  for f in $(php_ini_files "$v"); do
    mkdir -p "$(dirname "$f")"
    {
      echo "; DEVNANOTEK — php.ini ayarları. BU DOSYA OTOMATİK ÜRETİLİR."
      echo "; Değiştirmek için:  sudo devnanotek ini <ayar> <değer>   (ör. sudo devnanotek ini memory_limit 1G)"
      cat <<EOF
memory_limit = 512M
upload_max_filesize = 512M
post_max_size = 512M
max_file_uploads = 100
max_execution_time = 300
max_input_time = 300
max_input_vars = 10000
display_errors = On
display_startup_errors = On
log_errors = On
error_reporting = E_ALL & ~E_DEPRECATED
date.timezone = Europe/Istanbul
default_charset = UTF-8
short_open_tag = Off
output_buffering = 4096
opcache.enable = 1
opcache.enable_cli = 0
opcache.revalidate_freq = 0
opcache.validate_timestamps = 1
mysqli.allow_local_infile = On
EOF
      [ "$MAILPIT" = 1 ] && echo "sendmail_path = \"/usr/local/bin/mailpit sendmail -S 127.0.0.1:$SMTP_PORT\""
      if [ -s "$DN_ETC/php-overrides.ini" ]; then echo "; --- sizin ayarlarınız ---"; cat "$DN_ETC/php-overrides.ini"; fi
    } > "$f"
  done
}

set_cli_php() { # komut satırındaki 'php' = aktif sürüm
  local v="$1"
  if [ "$FAMILY" = deb ] && has update-alternatives; then
    quiet update-alternatives --set php "/usr/bin/php$v" || ln -sfn "/usr/bin/php$v" /usr/local/bin/php
  else
    ln -sfn "$(php_cli "$v")" /usr/local/bin/php; record links.txt /usr/local/bin/php
  fi
}

# -------------------------------------------------------- web sunucu ----
render() { # render KEY1 KEY2 ... < şablon  → __KEY__ yerine $KEY
  local s k; s="$(cat)"
  for k in "$@"; do s="${s//__${k}__/${!k}}"; done
  printf '%s\n' "$s"
}

vhost_docroot() { # Laravel/Symfony: kökte index yoksa ama public/index.php varsa public
  local d="$1"
  if [ ! -f "$d/index.php" ] && [ ! -f "$d/index.html" ] && [ -f "$d/public/index.php" ]; then echo "$d/public"; else echo "$d"; fi
}

write_web_config() {
  local SOCK DOCS="$DN_DOCS" ROOT="$DN_ROOT" ACCESS NG_ACCESS SSLON=0 HTTP="$HTTP_PORT" HTTPS="$HTTPS_PORT"
  local DBP="$DB_PORT" PGP="$PG_PORT" SMTPP="$SMTP_PORT" MAILP="$MAILPIT_UI_PORT" LOGD
  SOCK="$(php_sock "$PHP")"
  if [ "$LAN" = 1 ]; then ACCESS="all granted"; NG_ACCESS=""; else ACCESS="local"; NG_ACCESS="    allow 127.0.0.1; allow ::1; deny all;"; fi
  [ "$SSL" = 1 ] && [ -f "$DN_SSL/localhost.pem" ] && SSLON=1
  if [ "$WEB" = nginx ]; then
    write_nginx_config
  else
    write_apache_config
  fi
}

# Apache: izinler (Directory) sunucu düzeyinde yazılır — hem HTTP hem HTTPS sanal sunucuları için geçerli olur
apache_dir_block() { # $1 = klasör, $2 = Require değeri, $3 = AllowOverride
  local D="$1" REQ="$2" AO="${3:-All}"
  render D REQ AO SOCK <<'EOF'
<Directory "__D__">
    Options Indexes FollowSymLinks
    AllowOverride __AO__
    DirectoryIndex index.php index.html index.htm
    Require __REQ__
    <FilesMatch "\.(php|phtml)$">
        SetHandler "proxy:unix:__SOCK__|fcgi://localhost"
    </FilesMatch>
</Directory>
EOF
}

write_apache_config() {
  local LOGD="$APACHE_LOG" host dir VHOST VDIR CERT="$DN_SSL/localhost.pem" KEY="$DN_SSL/localhost-key.pem" PORT SSLLINES TOOLS
  local ENVS
  ENVS=$(render ROOT DBP PGP SMTPP MAILP <<'EOF'
    SetEnv DEVNANOTEK_ROOT "__ROOT__"
    SetEnv DEVNANOTEK_DB_PORT "__DBP__"
    SetEnv DEVNANOTEK_PG_PORT "__PGP__"
    SetEnv DEVNANOTEK_SMTP_PORT "__SMTPP__"
    SetEnv DEVNANOTEK_MAILPIT_PORT "__MAILP__"
EOF
)
  vhost_block() { # $1=ad $2=kök $3=ssl(0/1) $4=araçlar(0/1)
    VHOST="$1"; VDIR="$2"
    if [ "$3" = 1 ]; then PORT="$HTTPS"; SSLLINES=$(printf '    SSLEngine on\n    SSLCertificateFile "%s"\n    SSLCertificateKeyFile "%s"' "$CERT" "$KEY"); else PORT="$HTTP"; SSLLINES=""; fi
    if [ "$4" = 1 ]; then TOOLS=$(printf '    ServerAlias 127.0.0.1\n    Alias /phpmyadmin "%s/phpmyadmin"\n    Alias /adminer "%s/adminer"\n    IncludeOptional "%s/etc/apache-custom.conf"' "$DN_ROOT" "$DN_ROOT" "$DN_ROOT"); else TOOLS=""; fi
    render VHOST VDIR PORT SSLLINES TOOLS ENVS LOGD <<'EOF'

<VirtualHost *:__PORT__>
    ServerName __VHOST__
__TOOLS__
    DocumentRoot "__VDIR__"
__SSLLINES__
__ENVS__
    ErrorLog "__LOGD__/devnanotek-__VHOST__-error.log"
    CustomLog "__LOGD__/devnanotek-__VHOST__-access.log" combined
</VirtualHost>
EOF
  }
  {
    echo "# ============================================================================="
    echo "#  DEVNANOTEK — Apache yapılandırması. BU DOSYA OTOMATİK ÜRETİLİR (devnanotek apply)."
    echo "#  Kendi kurallarınız için: $DN_ROOT/etc/apache-custom.conf"
    echo "# ============================================================================="
    echo "ServerName localhost"
    apache_dir_block "$DN_DOCS" "$ACCESS" All
    apache_dir_block "$DN_ROOT/phpmyadmin" local None
    apache_dir_block "$DN_ROOT/adminer" local None
    printf '<Directory "%s/phpmyadmin/libraries">\n    Require all denied\n</Directory>\n<Directory "%s/phpmyadmin/templates">\n    Require all denied\n</Directory>\n' "$DN_ROOT" "$DN_ROOT"
    while read -r host dir; do   # httpdocs dışındaki alan adı klasörleri
      [ -n "$host" ] && [[ "$dir" != "$DN_DOCS"* ]] && apache_dir_block "$(vhost_docroot "$dir")" "$ACCESS" All
    done < <(cat "$DN_ETC/vhosts.list" 2>/dev/null)
    vhost_block localhost "$DN_DOCS" 0 1
    while read -r host dir; do [ -n "$host" ] && vhost_block "$host" "$(vhost_docroot "$dir")" 0 0; done < <(cat "$DN_ETC/vhosts.list" 2>/dev/null)
    if [ "$SSLON" = 1 ]; then
      vhost_block localhost "$DN_DOCS" 1 1
      while read -r host dir; do [ -n "$host" ] && vhost_block "$host" "$(vhost_docroot "$dir")" 1 0; done < <(cat "$DN_ETC/vhosts.list" 2>/dev/null)
    fi
  } | grep -v '^$' | sed 's/^<VirtualHost/\n<VirtualHost/' > "$APACHE_CONF"
  [ -f "$DN_ETC/apache-custom.conf" ] || echo "# DEVNANOTEK — Apache için kendi eklemeleriniz (asla üzerine yazılmaz)." > "$DN_ETC/apache-custom.conf"
  if [ "$FAMILY" = deb ]; then
    quiet a2enmod proxy proxy_fcgi setenvif rewrite headers alias
    [ "$SSLON" = 1 ] && quiet a2enmod ssl
    quiet a2ensite devnanotek
    if [ -e /etc/apache2/sites-enabled/000-default.conf ]; then quiet a2dissite 000-default && DEFAULT_SITE_DISABLED=1; fi
  else
    [ "$SSLON" = 1 ] && pkg_install mod_ssl
    # mod_ssl'in varsayılan sertifikası normalde Apache'nin ilk açılışında üretilir; yapılandırma testi ondan önce geçsin
    if [ -f /etc/httpd/conf.d/ssl.conf ] && [ ! -s /etc/pki/tls/certs/localhost.crt ]; then
      if [ -x /usr/libexec/httpd-ssl-gencerts ]; then quiet /usr/libexec/httpd-ssl-gencerts; else svc start httpd-init; fi
    fi
  fi
}

write_nginx_config() {
  local VHOST VDIR SNAME CERT="$DN_SSL/localhost.pem" KEY="$DN_SSL/localhost-key.pem" NAMES
  # ortak PHP konumu (fastcgi_param dizileri üst bloktan miras alınmaz)
  local PHPLOC
  PHPLOC=$(SOCK="$SOCK" ROOT="$DN_ROOT" DBP="$DBP" PGP="$PGP" SMTPP="$SMTPP" MAILP="$MAILP" render SOCK ROOT DBP PGP SMTPP MAILP <<'EOF'
        include fastcgi_params;
        fastcgi_param DEVNANOTEK_ROOT "__ROOT__";
        fastcgi_param DEVNANOTEK_DB_PORT "__DBP__";
        fastcgi_param DEVNANOTEK_PG_PORT "__PGP__";
        fastcgi_param DEVNANOTEK_SMTP_PORT "__SMTPP__";
        fastcgi_param DEVNANOTEK_MAILPIT_PORT "__MAILP__";
        fastcgi_read_timeout 600;
        fastcgi_pass unix:__SOCK__;
EOF
)
  server_block() { # $1=dinleme $2=ad $3=kök $4=ssl(0/1) $5=araçlar(0/1)
    echo "server {"
    if [ "$4" = 1 ]; then echo "    listen $HTTPS ssl;"; echo "    listen [::]:$HTTPS ssl;"
      echo "    ssl_certificate $CERT;"; echo "    ssl_certificate_key $KEY;"
    else echo "    listen $HTTP;"; echo "    listen [::]:$HTTP;"; fi
    echo "    server_name $2;"
    echo "    root \"$3\";"
    echo "    index index.php index.html index.htm;"
    echo "    autoindex on;"
    echo "    client_max_body_size 512m;"
    [ -n "$NG_ACCESS" ] && echo "$NG_ACCESS"
    if [ "$5" = 1 ]; then
      for t in phpmyadmin adminer; do
        echo "    location ^~ /$t {"
        echo "        alias \"$DN_ROOT/$t\";"
        echo "        index index.php;"
        echo "        allow 127.0.0.1; allow ::1; deny all;"
        echo "        location ~ \\.php\$ {"
        echo "            fastcgi_param SCRIPT_FILENAME \$request_filename;"
        echo "$PHPLOC" | sed 's/^/    /'
        echo "        }"
        echo "    }"
      done
    fi
    # localhost: klasör listesi (=404); alan adları: Laravel/WordPress gibi ön denetleyici (index.php)
    if [ "$5" = 1 ]; then echo "    location / { try_files \$uri \$uri/ =404; }"
    else echo "    location / { try_files \$uri \$uri/ /index.php?\$query_string; }"; fi
    echo "    location ~ \\.php\$ {"
    echo "        try_files \$uri =404;"
    echo "        fastcgi_param SCRIPT_FILENAME \$document_root\$fastcgi_script_name;"
    echo "$PHPLOC"
    echo "    }"
    echo "    location ~ /\\.(ht|git|env) { deny all; }"
    echo "}"
  }
  {
    echo "# DEVNANOTEK — Nginx yapılandırması. BU DOSYA OTOMATİK ÜRETİLİR (devnanotek apply)."
    server_block "$HTTP" "localhost 127.0.0.1" "$DN_DOCS" 0 1
    local host dir
    while read -r host dir; do
      [ -z "$host" ] && continue
      server_block "$HTTP" "$host" "$(vhost_docroot "$dir")" 0 0
    done < <(cat "$DN_ETC/vhosts.list" 2>/dev/null)
    if [ "$SSLON" = 1 ]; then
      server_block "$HTTPS" "localhost 127.0.0.1" "$DN_DOCS" 1 1
      while read -r host dir; do
        [ -z "$host" ] && continue
        server_block "$HTTPS" "$host" "$(vhost_docroot "$dir")" 1 0
      done < <(cat "$DN_ETC/vhosts.list" 2>/dev/null)
    fi
  } > "$NGINX_CONF"
}

test_web_config() {
  if [ "$WEB" = nginx ]; then nginx -t >>"$DN_LOG" 2>&1
  elif [ "$FAMILY" = deb ]; then apache2ctl configtest >>"$DN_LOG" 2>&1
  else httpd -t >>"$DN_LOG" 2>&1; fi
}

install_web() {
  if [ "$WEB" = nginx ]; then
    pkg_install nginx || return 1
    svc stop "$APACHE_SVC" 2>/dev/null; svc disable "$APACHE_SVC" 2>/dev/null
  else
    if [ "$FAMILY" = deb ]; then pkg_install apache2 || return 1; else pkg_install httpd || return 1; fi
    svc stop nginx 2>/dev/null; svc disable nginx 2>/dev/null
  fi
  svc enable "$(web_svc)"
}

# ------------------------------------------------------------ veritabanı --
db_svc() {
  case "$DB" in
    mariadb) echo mariadb ;;
    mysql) [ "$FAMILY" = deb ] && echo mysql || echo mysqld ;;
    *) echo "" ;;
  esac
}
db_client() { if has mariadb; then echo mariadb; else echo mysql; fi; }
pg_svc() {
  [ -z "$PG" ] && return
  if [ "$FAMILY" = deb ]; then [ "$HAS_SYSTEMD" = 1 ] && echo "postgresql@${PG}-main" || echo postgresql
  else echo "postgresql-$PG"; fi
}

install_db() {
  [ "$DB" = none ] && return 0
  local pre=0 srv
  if [ "$DB" = mariadb ]; then
    if [ -n "$DB_VER" ]; then
      info "MariaDB $DB_VER deposu ekleniyor (mariadb.org)..."
      run curl -fsSL -o /tmp/mariadb_repo_setup https://r.mariadb.com/downloads/mariadb_repo_setup || return 1
      run bash /tmp/mariadb_repo_setup --mariadb-server-version="mariadb-$DB_VER" --skip-maxscale || return 1
      [ -f /etc/apt/sources.list.d/mariadb.list ] && record added-repos.txt /etc/apt/sources.list.d/mariadb.list
      [ -f /etc/yum.repos.d/mariadb.repo ] && record added-repos.txt /etc/yum.repos.d/mariadb.repo
      pkg_update
    fi
    if [ "$FAMILY" = deb ]; then srv=mariadb-server; elif [ -n "$DB_VER" ]; then srv=MariaDB-server; else srv=mariadb-server; fi
    pkg_installed "$srv" && pre=1
    if [ "$FAMILY" = deb ]; then pkg_install mariadb-server mariadb-client || return 1
    elif [ -n "$DB_VER" ]; then pkg_install MariaDB-server MariaDB-client || return 1
    else pkg_install mariadb-server mariadb || return 1; fi
  else
    if [ "$FAMILY" = deb ]; then
      [ "$IS_UBUNTU" = 1 ] || { err "Debian'ın resmi depolarında MySQL yok. MariaDB seçin (MySQL ile uyumludur): sudo devnanotek install --db mariadb"; return 1; }
      pkg_installed mysql-server && pre=1
      pkg_install mysql-server mysql-client || return 1
    elif [ "$RPM_KIND" = fedora ]; then
      pkg_installed community-mysql-server && pre=1
      pkg_install community-mysql-server community-mysql || return 1
    else
      pkg_installed mysql-server && pre=1
      pkg_install mysql-server mysql || return 1
    fi
  fi
  [ "$pre" = 0 ] && DB_MANAGED=1
  write_db_cnf
  svc enable "$(db_svc)"; svc restart "$(db_svc)"
  sleep 2
  if [ "$DB_MANAGED" = 1 ]; then db_root_nopass; else warn "Bu bilgisayarda veritabanı zaten kuruluydu: root şifresine dokunulmadı. phpMyAdmin giriş ekranında kendi kullanıcı adınızla girin."; fi
  ok "$(db_title) hazır."
}

db_title() { case "$DB" in mariadb) echo "MariaDB $(db_version)" ;; mysql) echo "MySQL $(db_version)" ;; *) echo "Veritabanı yok" ;; esac; }
db_version() { $(db_client) --version 2>/dev/null | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -1; }

write_db_cnf() {
  local dir f bind=127.0.0.1
  [ "$LAN" = 1 ] && bind=0.0.0.0
  if [ "$FAMILY" = deb ]; then
    if [ "$DB" = mariadb ]; then dir=/etc/mysql/mariadb.conf.d; else dir=/etc/mysql/mysql.conf.d; fi
  else dir=/etc/my.cnf.d; fi
  mkdir -p "$dir"; f="$dir/99-devnanotek.cnf"
  {
    echo "# DEVNANOTEK — veritabanı ayarları (otomatik üretilir)"
    echo "[mysqld]"
    echo "bind-address = $bind"
    echo "port = $DB_PORT"
    echo "character-set-server = utf8mb4"
    [ "$DB" = mariadb ] && echo "collation-server = utf8mb4_unicode_ci"
    echo "max_allowed_packet = 256M"
    echo "# lower_case_table_names Linux'ta 0'dır (harf duyarlı) — canlı sunucuyla aynı davranır"
    echo "[client]"
    echo "default-character-set = utf8mb4"
  } > "$f"
}

db_root_nopass() { # yalnız bu bilgisayardan, şifresiz root (Windows sürümündeki gibi)
  local c; c="$(db_client)"
  if [ "$DB" = mariadb ]; then
    quiet "$c" -uroot -e "ALTER USER 'root'@'localhost' IDENTIFIED VIA unix_socket OR mysql_native_password USING PASSWORD('');
      CREATE USER IF NOT EXISTS 'root'@'127.0.0.1' IDENTIFIED BY ''; GRANT ALL PRIVILEGES ON *.* TO 'root'@'127.0.0.1' WITH GRANT OPTION;
      CREATE USER IF NOT EXISTS 'root'@'::1' IDENTIFIED BY ''; GRANT ALL PRIVILEGES ON *.* TO 'root'@'::1' WITH GRANT OPTION; FLUSH PRIVILEGES;" \
      || warn "MariaDB root kullanıcısı ayarlanamadı (ayrıntı: $DN_LOG)."
  else
    quiet "$c" -uroot -e "ALTER USER 'root'@'localhost' IDENTIFIED WITH caching_sha2_password BY '';
      CREATE USER IF NOT EXISTS 'root'@'127.0.0.1' IDENTIFIED WITH caching_sha2_password BY ''; GRANT ALL PRIVILEGES ON *.* TO 'root'@'127.0.0.1' WITH GRANT OPTION; FLUSH PRIVILEGES;" \
      || warn "MySQL root kullanıcısı ayarlanamadı (ayrıntı: $DN_LOG)."
  fi
}

install_pg() {
  local v="${1:-$PG_DEFAULT}" hba conf
  info "PostgreSQL $v kuruluyor (postgresql.org deposu)..."
  if [ "$FAMILY" = deb ]; then
    [ -n "$DEB_CODE" ] || { err "Bu dağıtım sürümü için PostgreSQL deposu bulunamadı."; return 1; }
    pkg_install postgresql-common || true
    install -d /usr/share/postgresql-common/pgdg
    run curl -fsSL -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc https://www.postgresql.org/media/keys/ACCC4CF8.asc || return 1
    echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt ${DEB_CODE}-pgdg main" > /etc/apt/sources.list.d/devnanotek-pgdg.list
    record added-repos.txt /etc/apt/sources.list.d/devnanotek-pgdg.list
    pkg_update
    pkg_install "postgresql-$v" || return 1
    hba="/etc/postgresql/$v/main/pg_hba.conf"; conf="/etc/postgresql/$v/main/conf.d/devnanotek.conf"
    mkdir -p "$(dirname "$conf")"
  else
    local repo
    if [ "$RPM_KIND" = el ]; then repo="https://download.postgresql.org/pub/repos/yum/reporpms/EL-${EL_VER}-$(uname -m)/pgdg-redhat-repo-latest.noarch.rpm"
    else repo="https://download.postgresql.org/pub/repos/yum/reporpms/F-${FEDORA_VER}-$(uname -m)/pgdg-fedora-repo-latest.noarch.rpm"; fi
    if ! pkg_installed pgdg-redhat-repo && ! pkg_installed pgdg-fedora-repo; then
      run dnf install -y "$repo" || return 1
      if [ "$RPM_KIND" = el ]; then record installed-packages.txt pgdg-redhat-repo; else record installed-packages.txt pgdg-fedora-repo; fi
    fi
    [ "$RPM_KIND" = el ] && [ "${EL_VER:-0}" -lt 10 ] && quiet dnf -qy module disable postgresql
    pkg_install "postgresql$v-server" || return 1
    [ -f "/var/lib/pgsql/$v/data/PG_VERSION" ] || run "/usr/pgsql-$v/bin/postgresql-$v-setup" initdb || return 1
    hba="/var/lib/pgsql/$v/data/pg_hba.conf"; conf="/var/lib/pgsql/$v/data/devnanotek.conf"
    grep -q "devnanotek.conf" "/var/lib/pgsql/$v/data/postgresql.conf" || echo "include_if_exists = 'devnanotek.conf'" >> "/var/lib/pgsql/$v/data/postgresql.conf"
  fi
  PG="$v"
  # yerel bağlantılar şifresiz (trust) — yalnız 127.0.0.1 / ::1; diğer kurallar aynen kalır
  if ! grep -q "DEVNANOTEK" "$hba"; then
    { echo "# DEVNANOTEK: bu bilgisayardan şifresiz bağlantı (geliştirme)"
      echo "host    all    all    127.0.0.1/32    trust"
      echo "host    all    all    ::1/128         trust"
      cat "$hba"; } > "$hba.tmp" && cat "$hba.tmp" > "$hba" && rm -f "$hba.tmp"
  fi
  printf "# DEVNANOTEK — otomatik üretilir\nlisten_addresses = 'localhost'\nport = %s\n" "$PG_PORT" > "$conf"
  chown postgres:postgres "$conf" 2>/dev/null
  svc enable "$(pg_svc)"; svc restart "$(pg_svc)"
  ok "PostgreSQL $v hazır (kullanıcı postgres, şifre gerekmez, port $PG_PORT)."
}

# --------------------------------------------------------------- araçlar --
github_latest_asset() { # github_latest_asset sahip/depo 'regex'
  curl -fsSL "https://api.github.com/repos/$1/releases/latest" 2>/dev/null | grep -oE '"browser_download_url": *"[^"]+"' | sed 's/.*"\(http[^"]*\)"$/\1/' | grep -E "$2" | head -1
}

install_pma() {
  local ver url
  ver=$(curl -fsSL https://www.phpmyadmin.net/home_page/version.json 2>/dev/null | grep -oE '"version": *"[0-9.]+"' | head -1 | grep -oE '[0-9.]+$')
  [ -n "$ver" ] || ver="5.2.3"
  url="https://files.phpmyadmin.net/phpMyAdmin/$ver/phpMyAdmin-$ver-all-languages.tar.gz"
  info "phpMyAdmin $ver indiriliyor..."
  run curl -fsSL -o /tmp/devnanotek-pma.tgz "$url" || return 1
  rm -rf "$DN_ROOT/phpmyadmin" "/tmp/phpMyAdmin-$ver-all-languages"
  run tar -xzf /tmp/devnanotek-pma.tgz -C /tmp || return 1
  mv "/tmp/phpMyAdmin-$ver-all-languages" "$DN_ROOT/phpmyadmin"
  echo "$ver" > "$DN_ROOT/phpmyadmin/.devnanotek-version"
  write_pma_config
  ok "phpMyAdmin $ver hazır: http://localhost/phpmyadmin"
}

write_pma_config() {
  [ -d "$DN_ROOT/phpmyadmin" ] || return 0
  local secret auth=config group
  secret="$(head -c 32 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c 32)"
  [ "$DB_MANAGED" = 1 ] || auth=cookie
  group="$(id -gn "$DN_USER" 2>/dev/null || echo "$DN_USER")"
  mkdir -p "$DN_TMP/phpmyadmin"; chown -R "$DN_USER:$group" "$DN_TMP/phpmyadmin"
  cat > "$DN_ROOT/phpmyadmin/config.inc.php" <<EOF
<?php
/** DEVNANOTEK — phpMyAdmin yapılandırması (otomatik üretilir). */
error_reporting(E_ALL & ~E_DEPRECATED & ~E_USER_DEPRECATED & ~E_NOTICE);
\$cfg['blowfish_secret'] = '$secret';
\$i = 1;
\$cfg['Servers'][\$i]['verbose'] = 'DEVNANOTEK';
\$cfg['Servers'][\$i]['auth_type'] = '$auth';
\$cfg['Servers'][\$i]['host'] = '127.0.0.1';
\$cfg['Servers'][\$i]['port'] = '$DB_PORT';
\$cfg['Servers'][\$i]['user'] = 'root';
\$cfg['Servers'][\$i]['password'] = '';
\$cfg['Servers'][\$i]['AllowNoPassword'] = true;
\$cfg['TempDir'] = '$DN_TMP/phpmyadmin';
\$cfg['DefaultLang'] = 'tr';
\$cfg['VersionCheck'] = false;
\$cfg['SendErrorReports'] = 'never';
\$cfg['ExecTimeLimit'] = 0;
EOF
}

install_adminer() {
  local url ver
  url="$(github_latest_asset vrana/adminer '/adminer-[0-9.]+\.php$')"
  [ -n "$url" ] || url="https://github.com/vrana/adminer/releases/download/v6.1.1/adminer-6.1.1.php"
  ver="$(echo "$url" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | tail -1)"
  mkdir -p "$DN_ROOT/adminer"
  run curl -fsSL -o "$DN_ROOT/adminer/adminer.php" "$url" || return 1
  echo "$ver" > "$DN_ROOT/adminer/version.txt"
  cat > "$DN_ROOT/adminer/index.php" <<'EOF'
<?php
/**
 * DEVNANOTEK — Adminer (MySQL/MariaDB, PostgreSQL, SQLite, SQL Server)
 * BU DOSYA OTOMATİK ÜRETİLİR. Yerel geliştirme için şifresiz girişe izin verir; yalnız bu bilgisayardan açılır.
 */
function adminer_object() {
    class DevnanotekAdminer extends Adminer\Adminer {
        function name() { return 'DEVNANOTEK Adminer'; }
        function login($login, $password) { return true; }
    }
    return new DevnanotekAdminer;
}
include __DIR__ . '/adminer.php';
EOF
  cat > "$DN_ROOT/adminer/go.php" <<'EOF'
<?php
/** DEVNANOTEK — Adminer'a tek tıkla giriş: go.php?db=mysql | pgsql | sqlite */
function dn_env(string $k, string $def): string { $v = getenv($k); if ($v === false || $v === '') $v = $_SERVER[$k] ?? $def; return (string)$v; }
$t = ['mysql' => ['server', '127.0.0.1:' . dn_env('DEVNANOTEK_DB_PORT', '3306'), 'root', ''],
      'pgsql' => ['pgsql', '127.0.0.1:' . dn_env('DEVNANOTEK_PG_PORT', '5432'), 'postgres', 'postgres']];
$k = $_GET['db'] ?? 'mysql';
if ($k === 'sqlite') { header('Location: ./?sqlite=&username=&db='); exit; }
if (!isset($t[$k])) $k = 'mysql';
[$driver, $server, $user, $db] = $t[$k];
?><!doctype html><meta charset="utf-8"><title>Adminer · DEVNANOTEK</title>
<form id="f" method="post" action="./">
<input type="hidden" name="auth[driver]" value="<?= htmlspecialchars($driver) ?>">
<input type="hidden" name="auth[server]" value="<?= htmlspecialchars($server) ?>">
<input type="hidden" name="auth[username]" value="<?= htmlspecialchars($user) ?>">
<input type="hidden" name="auth[password]" value="">
<input type="hidden" name="auth[db]" value="<?= htmlspecialchars($db) ?>">
<input type="hidden" name="token" id="token" value="">
<noscript><button type="submit">Adminer'a gir</button></noscript>
</form>
<script>fetch('./',{credentials:'same-origin'}).then(r=>r.text()).then(t=>{var m=t.match(/name=['"]token['"] value=['"]([^'"]+)['"]/);if(m)document.getElementById('token').value=m[1];document.getElementById('f').submit();}).catch(()=>document.getElementById('f').submit());</script>
Adminer açılıyor…
EOF
  ok "Adminer $ver hazır: http://localhost/adminer"
}

install_mailpit() {
  info "Mailpit kuruluyor..."
  run curl -fsSL -o /tmp/mailpit-install.sh https://raw.githubusercontent.com/axllent/mailpit/develop/install.sh || return 1
  run bash /tmp/mailpit-install.sh || return 1
  record links.txt /usr/local/bin/mailpit
  local group; group="$(id -gn "$DN_USER" 2>/dev/null || echo "$DN_USER")"
  mkdir -p "$DN_ROOT/mailpit"; chown "$DN_USER:$group" "$DN_ROOT/mailpit"
  if [ "$HAS_SYSTEMD" = 1 ]; then
    cat > /etc/systemd/system/devnanotek-mailpit.service <<EOF
[Unit]
Description=DEVNANOTEK Mailpit (test e-posta kutusu)
After=network.target

[Service]
User=$DN_USER
ExecStart=/usr/local/bin/mailpit --smtp 127.0.0.1:$SMTP_PORT --listen 127.0.0.1:$MAILPIT_UI_PORT --database $DN_ROOT/mailpit/mailpit.db --max 5000 --smtp-auth-accept-any --smtp-auth-allow-insecure
Restart=on-failure

[Install]
WantedBy=multi-user.target
EOF
    quiet systemctl daemon-reload
    svc enable devnanotek-mailpit; svc restart devnanotek-mailpit
  fi
  ok "Mailpit hazır: http://localhost:$MAILPIT_UI_PORT (SMTP 127.0.0.1:$SMTP_PORT)"
}

install_node() {
  local major="${1:-$NODE_DEFAULT}" arch file ver sums
  case "$(uname -m)" in x86_64) arch=x64 ;; aarch64|arm64) arch=arm64 ;; *) warn "Node.js bu mimari için kurulamadı."; return 1 ;; esac
  sums="$(curl -fsSL "https://nodejs.org/dist/latest-v${major}.x/SHASUMS256.txt")" || { err "Node.js $major bulunamadı."; return 1; }
  file="$(echo "$sums" | awk '{print $2}' | grep -E "^node-v[0-9.]+-linux-${arch}\.tar\.xz$" | head -1)"
  [ -n "$file" ] || { err "Node.js $major ($arch) paketi bulunamadı."; return 1; }
  ver="${file#node-v}"; ver="${ver%%-linux*}"
  info "Node.js $ver indiriliyor..."
  run curl -fsSL -o "/tmp/$file" "https://nodejs.org/dist/latest-v${major}.x/$file" || return 1
  (cd /tmp && echo "$sums" | grep " $file\$" | sha256sum -c - >/dev/null 2>&1) || { err "Node.js paketinin sağlaması tutmadı."; return 1; }
  mkdir -p "$DN_ROOT/node"; rm -rf "$DN_ROOT/node/$ver"
  run tar -xJf "/tmp/$file" -C "$DN_ROOT/node" || return 1
  mv "$DN_ROOT/node/${file%.tar.xz}" "$DN_ROOT/node/$ver"
  ln -sfn "$DN_ROOT/node/$ver" "$DN_ROOT/node/current"
  local b
  for b in node npm npx corepack; do
    if [ -e "/usr/local/bin/$b" ] && [ ! -L "/usr/local/bin/$b" ]; then warn "/usr/local/bin/$b başka bir kurulumdan; dokunulmadı."; continue; fi
    ln -sfn "$DN_ROOT/node/current/bin/$b" "/usr/local/bin/$b"; record links.txt "/usr/local/bin/$b"
  done
  NODE="$ver"
  ok "Node.js $ver hazır (node, npm, npx)."
}

install_composer() {
  local expected actual
  expected="$(curl -fsSL https://composer.github.io/installer.sig)" || return 1
  run curl -fsSL -o /tmp/composer-setup.php https://getcomposer.org/installer || return 1
  actual="$(sha384sum /tmp/composer-setup.php | awk '{print $1}')"
  [ "$expected" = "$actual" ] || { err "Composer kurucusunun imzası tutmadı."; return 1; }
  run "$(php_cli "$PHP")" /tmp/composer-setup.php --install-dir=/usr/local/bin --filename=composer --quiet || return 1
  record links.txt /usr/local/bin/composer
  ok "Composer hazır."
}

install_mkcert() {
  local arch; case "$(uname -m)" in x86_64) arch=amd64 ;; aarch64|arm64) arch=arm64 ;; *) warn "mkcert bu mimari için yok."; return 1 ;; esac
  run curl -fsSL -o /usr/local/bin/mkcert "https://dl.filippo.io/mkcert/latest?for=linux/$arch" || return 1
  chmod +x /usr/local/bin/mkcert; record links.txt /usr/local/bin/mkcert
  if [ "$FAMILY" = deb ]; then pkg_install libnss3-tools; else pkg_install nss-tools; fi
  mkdir -p "$DN_SSL/ca"
  quiet env CAROOT="$DN_SSL/ca" mkcert -install
  # tarayıcıların (Firefox/Chrome) kullanıcı sertifika deposu
  [ -n "$DN_USER" ] && [ "$DN_USER" != root ] && quiet sudo -u "$DN_USER" env CAROOT="$DN_SSL/ca" mkcert -install
  SSL=1
  make_certs
  ok "Yerel HTTPS hazır: https://localhost"
}

make_certs() {
  [ "$SSL" = 1 ] && has mkcert || return 0
  local names=(localhost 127.0.0.1 ::1) h
  while read -r h _; do [ -n "$h" ] && names+=("$h"); done < <(cat "$DN_ETC/vhosts.list" 2>/dev/null)
  quiet env CAROOT="$DN_SSL/ca" mkcert -cert-file "$DN_SSL/localhost.pem" -key-file "$DN_SSL/localhost-key.pem" "${names[@]}"
  chmod 644 "$DN_SSL/localhost.pem"; chmod 640 "$DN_SSL/localhost-key.pem"
  chgrp "$(web_user)" "$DN_SSL/localhost-key.pem" 2>/dev/null
}

# ----------------------------------------------------------- SELinux ----
selinux_setup() {
  has selinuxenabled && selinuxenabled || return 0
  info "SELinux etiketleri ayarlanıyor..."
  pkg_install policycoreutils-python-utils || return 0
  local p
  for p in "$DN_ROOT/httpdocs(/.*)?" "$DN_ROOT/tmp(/.*)?" "$DN_ROOT/logs(/.*)?" "$DN_ROOT/mailpit(/.*)?"; do
    quiet semanage fcontext -a -t httpd_sys_rw_content_t "$p" || quiet semanage fcontext -m -t httpd_sys_rw_content_t "$p"
  done
  for p in "$DN_ROOT/phpmyadmin(/.*)?" "$DN_ROOT/adminer(/.*)?" "$DN_ROOT/ssl(/.*)?"; do
    quiet semanage fcontext -a -t httpd_sys_content_t "$p" || quiet semanage fcontext -m -t httpd_sys_content_t "$p"
  done
  quiet restorecon -R "$DN_ROOT"
  quiet setsebool -P httpd_can_network_connect_db 1 httpd_can_network_connect 1 httpd_can_sendmail 1
  SELINUX_DONE=1
}

# --------------------------------------------------------------- hosts ----
write_hosts() {
  local tmp; tmp="$(mktemp)"
  awk -v b="$HOSTS_BEGIN" -v e="$HOSTS_END" '$0==b{skip=1;next} $0==e{skip=0;next} !skip' /etc/hosts > "$tmp"
  if [ -s "$DN_ETC/vhosts.list" ]; then
    { echo "$HOSTS_BEGIN"; awk 'NF {print "127.0.0.1  "$1"\n::1        "$1}' "$DN_ETC/vhosts.list"; echo "$HOSTS_END"; } >> "$tmp"
  fi
  cat "$tmp" > /etc/hosts; rm -f "$tmp"
}

# --------------------------------------------------------- uygula / akış --
apply_config() {
  [ -n "$PHP" ] || die "PHP seçili değil. Kurulum: sudo devnanotek install"
  info "Yapılandırma yazılıyor..."
  write_php_pool "$PHP"; write_php_ini "$PHP"
  local v; for v in $(php_installed_list); do [ "$v" = "$PHP" ] || rm -f "$(php_pool_dir "$v")/devnanotek.conf"; done
  make_certs
  write_web_config
  write_hosts
  write_pma_config
  write_landing
  [ "$SELINUX_DONE" = 1 ] && quiet restorecon -R "$DN_ROOT"
  if ! test_web_config; then err "Web sunucu yapılandırması hatalı (ayrıntı: $DN_LOG)."; return 1; fi
  save_state
  svc restart "$(php_fpm_svc "$PHP")"
  for v in $(php_installed_list); do [ "$v" = "$PHP" ] || svc restart "$(php_fpm_svc "$v")"; done
  svc restart "$(web_svc)"
  ok "Uygulandı."
}

managed_services() {
  echo "$(web_svc)"; echo "$(php_fpm_svc "$PHP")"
  [ "$DB" != none ] && db_svc
  [ -n "$PG" ] && pg_svc
  [ "$MAILPIT" = 1 ] && [ "$HAS_SYSTEMD" = 1 ] && echo devnanotek-mailpit
  return 0
}

ensure_dirs() {
  mkdir -p "$DN_ROOT" "$DN_DOCS" "$DN_ETC" "$DN_LOG_DIR" "$DN_BACKUPS" "$DN_SSL" "$DN_TMP"
  local group; group="$(id -gn "$DN_USER" 2>/dev/null || echo "$DN_USER")"
  chown "$DN_USER:$group" "$DN_DOCS" "$DN_BACKUPS" 2>/dev/null
  chmod 755 "$DN_ROOT" "$DN_DOCS"
}

write_landing() { # Windows sürümüyle aynı açılış sayfası; önceki DEVNANOTEK sayfası yedeklenip güncellenir, sizin index'inize dokunulmaz
  local f="$DN_DOCS/index.php" tmp
  [ -e "$DN_DOCS/index.html" ] && [ ! -e "$f" ] && return 0
  [ -e "$f" ] && ! grep -q "localhost açılış sayfası" "$f" 2>/dev/null && return 0
  tmp="$(mktemp)"
  cat > "$tmp" <<'EOF'
<?php
/**
 * DEVNANOTEK — localhost açılış sayfası
 * Bu dosyayı silebilir veya değiştirebilirsiniz. (Ayarlar > Sistem > "Açılış sayfasını geri yükle" ile geri gelir.)
 */
if (isset($_GET['phpinfo'])) { phpinfo(); exit; }

function dn_env(string $k, string $def): string {
    $v = getenv($k);
    if ($v === false || $v === '') $v = $_SERVER[$k] ?? $_SERVER['REDIRECT_' . $k] ?? $def;
    return (string)$v;
}
$root     = __DIR__;
$dnRoot   = rtrim(str_replace('\\', '/', dn_env('DEVNANOTEK_ROOT', dirname(__DIR__))), '/');
$dbPort   = (int)dn_env('DEVNANOTEK_DB_PORT', '3306');
$pgPort   = (int)dn_env('DEVNANOTEK_PG_PORT', '5432');
$smtpPort = (int)dn_env('DEVNANOTEK_SMTP_PORT', '1025');
$mailPort = (int)dn_env('DEVNANOTEK_MAILPIT_PORT', '8025');
$host     = $_SERVER['HTTP_HOST'] ?? 'localhost';
$hostOnly = preg_replace('/:\d+$/', '', $host);
$https    = !empty($_SERVER['HTTPS']) && $_SERVER['HTTPS'] !== 'off';
$isWin    = DIRECTORY_SEPARATOR === '\\';
$hasPma     = is_dir("$dnRoot/bin/phpmyadmin") || is_dir("$dnRoot/phpmyadmin");
$hasAdminer = is_dir("$dnRoot/bin/adminer") || is_dir("$dnRoot/adminer");

// ---- dil: DEVNANOTEK'in arayüz dili (etc/language), yoksa tarayıcının dili ----
$lang = strtolower(trim((string)@file_get_contents("$dnRoot/etc/language")));
if ($lang !== 'tr' && $lang !== 'en') $lang = stripos($_SERVER['HTTP_ACCEPT_LANGUAGE'] ?? '', 'tr') === 0 ? 'tr' : 'en';
$EN = [
    'PHP mysqli eklentisi kapalı' => 'The PHP mysqli extension is off',
    'Veritabanı çalışmıyor — DEVNANOTEK\'ten başlatın' => 'The database isn\'t running — start it from DEVNANOTEK',
    'Çalışıyor · root şifresi tanımlı (phpMyAdmin\'de girin)' => 'Running · a root password is set (enter it in phpMyAdmin)',
    'Bağlanılamadı' => 'Couldn\'t connect',
    'Çalışıyor · bağlanılamadı' => 'Running · couldn\'t connect',
    'Çalışıyor · PHP pgsql eklentisi kapalı' => 'Running · the PHP pgsql extension is off',
    '%d eklenti' => '%d extensions',
    'Web sunucu' => 'Web server',
    'Çalışıyor' => 'Running',
    'root kullanıcısı · şifresiz' => 'user root · no password',
    'postgres kullanıcısı · şifresiz' => 'user postgres · no password',
    'Test e-posta · Mailpit' => 'Test email · Mailpit',
    'Giden e-postalar burada toplanır' => 'Outgoing emails are collected here',
    'Mailpit çalışmıyor' => 'Mailpit isn\'t running',
    'Gelen kutusu' => 'Inbox',
    'Projeler' => 'Projects',
    'Henüz proje yok.' => 'No projects yet.',
    'Belge kökünün içine bir klasör ekleyin:' => 'Add a folder inside the document root:',
    '+%d daha' => '+%d more',
    'Yüklü eklentiler (%d)' => 'Loaded extensions (%d)',
];
$t = fn(string $s): string => $lang === 'en' ? ($EN[$s] ?? $s) : $s;

function dn_port_open(int $port): bool {
    $s = @fsockopen('127.0.0.1', $port, $no, $str, 0.3);
    if ($s) { fclose($s); return true; }
    return false;
}

// ---- MariaDB / MySQL ----
$db = ['ok' => false, 'ver' => '', 'msg' => ''];
if (!function_exists('mysqli_connect')) {
    $db['msg'] = $t('PHP mysqli eklentisi kapalı');
} elseif (!dn_port_open($dbPort)) {
    $db['msg'] = $t('Veritabanı çalışmıyor — DEVNANOTEK\'ten başlatın');
} else {
    mysqli_report(MYSQLI_REPORT_OFF);
    $c = @mysqli_connect('127.0.0.1', 'root', '', '', $dbPort);
    if ($c) { $db['ok'] = true; $db['ver'] = mysqli_get_server_info($c); mysqli_close($c); }
    elseif (mysqli_connect_errno() === 1045) { $db['msg'] = $t('Çalışıyor · root şifresi tanımlı (phpMyAdmin\'de girin)'); $db['ok'] = null; }
    else { $db['msg'] = mysqli_connect_error() ?: $t('Bağlanılamadı'); }
}

// ---- PostgreSQL (isteğe bağlı: yalnız port açıksa gösterilir) ----
$pg = null;
if (dn_port_open($pgPort)) {
    $pg = ['ok' => false, 'ver' => '', 'msg' => ''];
    if (function_exists('pg_connect')) {
        $pc = @pg_connect('host=127.0.0.1 port=' . $pgPort . ' user=postgres dbname=postgres connect_timeout=2');
        if ($pc) { $pg['ok'] = true; $pg['ver'] = pg_parameter_status($pc, 'server_version') ?: ''; pg_close($pc); }
        else { $pg['msg'] = $t('Çalışıyor · bağlanılamadı'); }
    } else { $pg['msg'] = $t('Çalışıyor · PHP pgsql eklentisi kapalı'); }
}
$mailUp = dn_port_open($smtpPort);

function dn_dirs(string $dir): array {
    $out = [];
    foreach (@scandir($dir) ?: [] as $d) {
        if ($d === '.' || $d === '..' || $d[0] === '.' || $d[0] === '_') continue;
        if (is_dir("$dir/$d")) $out[] = $d;
    }
    natcasesort($out);
    return array_values($out);
}
function dn_link(string $rel, string $abs): string {
    $hasIndex = is_file("$abs/index.php") || is_file("$abs/index.html") || is_file("$abs/index.htm");
    $enc = implode('/', array_map('rawurlencode', explode('/', $rel)));
    if (!$hasIndex && is_file("$abs/public/index.php")) return "/$enc/public/";
    return "/$enc/";
}
$projects = dn_dirs($root);
$ext = get_loaded_extensions(); sort($ext, SORT_FLAG_CASE | SORT_STRING);
$h = fn($s) => htmlspecialchars((string)$s, ENT_QUOTES, 'UTF-8');
?>
<!doctype html>
<html lang="<?= $lang ?>">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>DEVNANOTEK · localhost</title>
<link rel="icon" type="image/svg+xml" href="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64'%3E%3Crect width='64' height='64' rx='16' fill='%23232425'/%3E%3Cpath d='M16 14h17c11 0 18 7 18 18S44 50 33 50H16V14zm10 9v18h6c6 0 9-3 9-9s-3-9-9-9h-6z' fill='%23FAA41A'/%3E%3C/svg%3E">
<style>
  /* DEVNANOTEK paleti (devnanotek.net) — gündüz / gece */
  :root{--bg:#FAF9F7;--card:#FFFFFF;--line:#E7E1D5;--txt:#232425;--muted:#6E675B;--acc:#9A6400;--gold:#FAA41A;--chip:#F4F1EB;--ok:#22B573;--warn:#F5A524;--bad:#E5484D;--shadow:0 1px 2px rgba(35,36,37,.05)}
  @media (prefers-color-scheme: dark){:root{--bg:#1B1C1D;--card:#232425;--line:#333436;--txt:#F2EFE8;--muted:#A49B8C;--acc:#FFB43A;--chip:#2A2B2D;--shadow:none}}
  *{box-sizing:border-box}
  body{margin:0;font:15px/1.55 "Segoe UI",system-ui,-apple-system,Roboto,"Helvetica Neue",Arial,sans-serif;background:var(--bg);color:var(--txt);-webkit-font-smoothing:antialiased}
  a{color:var(--acc);text-decoration:none}
  a:hover{text-decoration:underline}
  .wrap{max-width:1120px;margin:0 auto;padding:28px 20px 56px}
  header{display:flex;align-items:center;gap:14px;flex-wrap:wrap;margin-bottom:26px}
  .mark{width:48px;height:48px;flex:none;display:block}
  .brand{display:flex;flex-direction:column;gap:6px;min-width:0}
  .wordmark{height:26px;width:auto;display:block}
  .wordmark .n{fill:var(--txt)}
  .sub{color:var(--muted);font-size:13px}
  .pill{display:inline-block;background:var(--chip);border:1px solid var(--line);border-radius:999px;padding:1px 9px;font-size:12px;color:var(--muted);margin-left:6px;vertical-align:1px}
  code{font:12.5px/1.4 "Cascadia Mono",Consolas,ui-monospace,monospace;background:var(--chip);border:1px solid var(--line);border-radius:6px;padding:1px 6px;word-break:break-all}
  .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(240px,1fr));gap:14px}
  .card{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:16px 18px;box-shadow:var(--shadow);min-width:0}
  .label{color:var(--muted);font-size:12.5px;font-weight:600;letter-spacing:.02em;margin-bottom:6px}
  .val{font-weight:600;font-size:16px;overflow-wrap:anywhere}
  .note{color:var(--muted);font-size:13px;margin-top:2px;overflow-wrap:anywhere}
  .dot{display:inline-block;width:9px;height:9px;border-radius:50%;margin-right:7px;vertical-align:1px;background:var(--bad)}
  .dot.ok{background:var(--ok)}.dot.warn{background:var(--warn)}
  .links{display:flex;flex-wrap:wrap;gap:6px;margin-top:10px}
  .btn{display:inline-flex;align-items:center;gap:6px;background:var(--chip);border:1px solid var(--line);border-radius:9px;padding:5px 11px;font-size:13px;font-weight:600;color:var(--acc)}
  .btn:hover{text-decoration:none;border-color:var(--gold)}
  h2{font-size:15px;margin:32px 0 12px;color:var(--muted);font-weight:600;display:flex;align-items:center;gap:8px}
  .proj{display:flex;flex-direction:column;gap:10px}
  .proj .name{display:flex;align-items:center;gap:9px;font-weight:700;font-size:15.5px;color:var(--txt)}
  .proj .name svg{flex:none;width:20px;height:20px;fill:var(--gold)}
  .chips{display:flex;flex-wrap:wrap;gap:6px}
  .chips a{background:var(--chip);border:1px solid var(--line);border-radius:999px;padding:2px 10px;font-size:12.5px}
  .empty{color:var(--muted);padding:28px;text-align:center;border:1px dashed var(--line);border-radius:14px;line-height:1.8}
  details.card summary{cursor:pointer;font-weight:600;list-style:none;display:flex;justify-content:space-between;align-items:center}
  details.card summary::-webkit-details-marker{display:none}
  details.card summary::after{content:"＋";color:var(--muted)}
  details[open].card summary::after{content:"－"}
  .ext{color:var(--muted);font-size:12.5px;line-height:1.9;margin-top:10px}
  footer{margin-top:40px;color:var(--muted);font-size:12.5px;display:flex;justify-content:space-between;flex-wrap:wrap;gap:8px}
</style>
</head>
<body>
<div class="wrap">
  <header>
    <svg class="mark" viewBox="0 0 64 64" aria-hidden="true"><rect width="64" height="64" rx="16" fill="#232425"/><path d="M16 14h17c11 0 18 7 18 18S44 50 33 50H16V14zm10 9v18h6c6 0 9-3 9-9s-3-9-9-9h-6z" fill="#FAA41A"/></svg>
    <div class="brand">
      <!-- DEVNANOTEK yazı logosu (Bebas Neue çizimi — yazı tipi kurulu olmasa da aynı görünür) -->
      <svg class="wordmark" viewBox="4 24 426 72" role="img" aria-label="DEVNANOTEK">
        <path fill="#FAA41A" d="M15.1,35L15.1,85 20.7,85C22.5,85 23.9,84.5 24.9,83.4 25.8,82.3 26.3,80.6 26.3,78.2L26.3,41.8C26.3,39.4 25.8,37.7 24.9,36.6 23.9,35.5 22.5,35 20.7,35L15.1,35z M4.1,25L20.9,25C26.4,25 30.5,26.5 33.2,29.4 35.9,32.3 37.3,36.6 37.3,42.3L37.3,77.7C37.3,83.4 35.9,87.7 33.2,90.6 30.5,93.5 26.4,95 20.9,95L4.1,95 4.1,25z M48.7,25L78.7,25 78.7,35 59.7,35 59.7,53.5 74.8,53.5 74.8,63.5 59.7,63.5 59.7,85 78.7,85 78.7,95 48.7,95 48.7,25z M86.1,25L97.2,25 104.4,79.3 104.6,79.3 111.8,25 121.9,25 111.3,95 96.7,95 86.1,25z"/>
        <path class="n" d="M131.2,25L145,25 155.7,66.9 155.9,66.9 155.9,25 165.7,25 165.7,95 154.4,95 141.2,43.9 141,43.9 141,95 131.2,95 131.2,25z M193.3,37.2L188.5,71.8 198.4,71.8 193.5,37.2 193.3,37.2z M186.4,25L201.3,25 212.7,95 201.7,95 199.7,81.1 199.7,81.3 187.2,81.3 185.2,95 175,95 186.4,25z M222,25L235.8,25 246.5,66.9 246.7,66.9 246.7,25 256.5,25 256.5,95 245.2,95 232,43.9 231.8,43.9 231.8,95 222,95 222,25z M284.6,34C280.8,34,278.9,36.3,278.9,40.9L278.9,79.1C278.9,83.7 280.8,86 284.6,86 288.4,86 290.3,83.7 290.3,79.1L290.3,40.9C290.3,36.3,288.4,34,284.6,34z M284.6,24C290,24 294.1,25.5 297,28.6 299.9,31.7 301.3,36 301.3,41.6L301.3,78.4C301.3,84 299.9,88.3 297,91.4 294.1,94.5 290,96 284.6,96 279.2,96 275.1,94.5 272.2,91.4 269.3,88.3 267.9,84 267.9,78.4L267.9,41.6C267.9,36 269.3,31.7 272.2,28.6 275.1,25.5 279.2,24 284.6,24z M309.8,25L343.8,25 343.8,35 332.3,35 332.3,95 321.3,95 321.3,35 309.8,35 309.8,25z M353.1,25L383.1,25 383.1,35 364.1,35 364.1,53.5 379.2,53.5 379.2,63.5 364.1,63.5 364.1,85 383.1,85 383.1,95 353.1,95 353.1,25z M393.4,25L404.4,25 404.4,54.5 418.4,25 429.4,25 416.3,50.7 429.6,95 418.1,95 408.8,63.8 404.4,72.7 404.4,95 393.4,95 393.4,25z"/>
      </svg>
      <div class="sub">Local Server <span class="pill"><?= $isWin ? 'Windows' : 'Linux' ?></span></div>
    </div>
  </header>

  <div class="grid">
    <div class="card">
      <div class="label">PHP</div>
      <div class="val"><?= PHP_VERSION ?></div>
      <div class="note"><?= $h(PHP_SAPI) ?> · <?= $h(sprintf($t('%d eklenti'), count($ext))) ?></div>
      <div class="links"><a class="btn" href="/?phpinfo=1">phpinfo()</a></div>
    </div>
    <div class="card">
      <div class="label"><?= $h($t('Web sunucu')) ?></div>
      <div class="val"><span class="dot ok"></span><?= $h(explode(' ', $_SERVER['SERVER_SOFTWARE'] ?? $t('Web sunucu'))[0]) ?></div>
      <div class="note"><?= $h($host) ?> · <?= $https ? 'HTTPS' : 'HTTP' ?></div>
    </div>
    <div class="card">
      <div class="label">MariaDB / MySQL · port <?= $dbPort ?></div>
      <div class="val"><span class="dot <?= $db['ok'] ? 'ok' : ($db['ok'] === null ? 'warn' : '') ?>"></span><?= $db['ok'] ? $h($db['ver']) : ($db['ok'] === null ? $h($t('Çalışıyor')) : $h($t('Bağlanılamadı'))) ?></div>
      <div class="note"><?= $db['ok'] ? $h($t('root kullanıcısı · şifresiz')) : $h($db['msg']) ?></div>
      <div class="links">
        <?php if ($hasPma): ?><a class="btn" href="/phpmyadmin/">phpMyAdmin</a><?php endif; ?>
        <?php if ($hasAdminer): ?><a class="btn" href="/adminer/go.php?db=mysql">Adminer</a><?php endif; ?>
      </div>
    </div>
    <?php if ($pg !== null): ?>
    <div class="card">
      <div class="label">PostgreSQL · port <?= $pgPort ?></div>
      <div class="val"><span class="dot <?= $pg['ok'] ? 'ok' : 'warn' ?>"></span><?= $pg['ok'] ? $h($pg['ver']) : $h($t('Çalışıyor')) ?></div>
      <div class="note"><?= $pg['ok'] ? $h($t('postgres kullanıcısı · şifresiz')) : $h($pg['msg']) ?></div>
      <?php if ($hasAdminer): ?><div class="links"><a class="btn" href="/adminer/go.php?db=pgsql">Adminer</a></div><?php endif; ?>
    </div>
    <?php endif; ?>
    <div class="card">
      <div class="label"><?= $h($t('Test e-posta · Mailpit')) ?></div>
      <div class="val"><span class="dot <?= $mailUp ? 'ok' : 'warn' ?>"></span>SMTP 127.0.0.1:<?= $smtpPort ?></div>
      <div class="note"><?= $h($mailUp ? $t('Giden e-postalar burada toplanır') : $t('Mailpit çalışmıyor')) ?></div>
      <div class="links"><a class="btn" href="http://<?= $h($hostOnly) ?>:<?= $mailPort ?>/" target="_blank" rel="noopener"><?= $h($t('Gelen kutusu')) ?></a></div>
    </div>
  </div>

  <h2><?= $h($t('Projeler')) ?> <code><?= $h($root) ?></code></h2>
  <?php if (!$projects): ?>
    <div class="empty"><?= $h($t('Henüz proje yok.')) ?><br><?= $h($t('Belge kökünün içine bir klasör ekleyin:')) ?> <code>GITHUB/proje1</code> → <a href="/GITHUB/proje1/">localhost/GITHUB/proje1</a></div>
  <?php else: ?>
  <div class="grid">
    <?php foreach ($projects as $p): $abs = "$root/$p"; $subs = dn_dirs($abs); ?>
      <div class="card proj">
        <a class="name" href="<?= $h(dn_link($p, $abs)) ?>"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M10 4l2 2h8a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h6z"/></svg><?= $h($p) ?></a>
        <?php if ($subs): ?>
        <div class="chips">
          <?php foreach (array_slice($subs, 0, 24) as $s): ?>
            <a href="<?= $h(dn_link("$p/$s", "$abs/$s")) ?>"><?= $h($s) ?></a>
          <?php endforeach; ?>
          <?php if (count($subs) > 24): ?><span class="note"><?= $h(sprintf($t('+%d daha'), count($subs) - 24)) ?></span><?php endif; ?>
        </div>
        <?php endif; ?>
      </div>
    <?php endforeach; ?>
  </div>
  <?php endif; ?>

  <h2>PHP</h2>
  <details class="card">
    <summary><?= $h(sprintf($t('Yüklü eklentiler (%d)'), count($ext))) ?></summary>
    <div class="ext"><?= $h(implode(' · ', $ext)) ?></div>
  </details>

  <footer>
    <span>DEVNANOTEK Local Server</span>
    <a href="https://devnanotek.net" target="_blank" rel="noopener">devnanotek.net</a>
  </footer>
</div>
</body>
</html>
EOF
  if [ -e "$f" ] && cmp -s "$tmp" "$f"; then rm -f "$tmp"; return 0; fi   # zaten güncel
  [ -e "$f" ] && cp -f "$f" "$f.yedek-$(date +%Y%m%d-%H%M%S)"            # eski DEVNANOTEK sayfası yedeklenir
  mv -f "$tmp" "$f"; chmod 644 "$f"
  chown "$DN_USER:$(id -gn "$DN_USER" 2>/dev/null || echo "$DN_USER")" "$f"
}

self_install() {
  local src; src="$(readlink -f "${BASH_SOURCE[0]}" 2>/dev/null)"
  [ -f "$src" ] || die "Betik bir dosyadan çalıştırılmalı: önce indirin, sonra 'sudo bash devnanotek.sh install'."
  if [ "$src" != "$DN_ROOT/devnanotek.sh" ]; then cp -f "$src" "$DN_ROOT/devnanotek.sh"; fi
  chmod 755 "$DN_ROOT/devnanotek.sh"
  ln -sfn "$DN_ROOT/devnanotek.sh" "$DN_LINK"
}

# ============================================================ KOMUTLAR ====
cmd_install() {
  need_root install
  detect_os; require_supported
  load_state
  local a; local want_pg="" want_node=1
  while [ $# -gt 0 ]; do
    case "$1" in
      --php) PHP="$2"; shift ;;
      --web) WEB="$2"; shift ;;
      --db) DB="$2"; shift ;;
      --db-version) DB_VER="$2"; shift ;;
      --pg|--postgres) want_pg="${2:-$PG_DEFAULT}"; shift ;;
      --node) want_node="$2"; shift ;;
      --no-node) want_node=0 ;;
      --no-mail) MAILPIT=0 ;;
      --no-ssl) SSL=0; NO_SSL=1 ;;
      --mode) MODE="$2"; shift ;;
      --user) DN_USER="$2"; shift ;;
      -y|--yes) DN_YES=1 ;;
      *) die "Bilinmeyen seçenek: $1 (yardım: devnanotek help)" ;;
    esac
    shift
  done
  [ -n "$DN_USER" ] || DN_USER="${SUDO_USER:-}"
  if [ -z "$DN_USER" ] || [ "$DN_USER" = root ]; then
    DN_USER="$(logname 2>/dev/null)"; [ -z "$DN_USER" ] || [ "$DN_USER" = root ] && DN_USER="$(web_user)"
    warn "Projelerin sahibi olarak '$DN_USER' kullanılacak (normal kullanıcınızla 'sudo' ile çalıştırmanız önerilir)."
  fi
  id "$DN_USER" >/dev/null 2>&1 || die "Kullanıcı bulunamadı: $DN_USER"

  banner
  echo "  Sistem   : $OS_NAME ($ARCH)"
  echo "  Kök      : $DN_ROOT   (projeler: $DN_DOCS)"
  echo "  Kullanıcı: $DN_USER"
  echo
  mkdir -p "$DN_LOG_DIR"; log "==== kurulum başladı ($OS_NAME) ===="
  base_tools || die "Temel araçlar kurulamadı."
  setup_php_repo || die "PHP deposu eklenemedi."

  # ---- sihirbaz (seçenek verilmediyse ve terminal etkileşimliyse) ----
  if [ -z "$DN_YES" ] && [ -t 0 ] && has whiptail; then
    local avail list=() v
    avail="$(php_available_list)"
    for v in $avail; do list+=("$v" "PHP $v" "$([ "$v" = "${PHP:-$PHP_DEFAULT}" ] && echo ON || echo OFF)"); done
    [ ${#list[@]} -gt 0 ] || die "Depoda kurulabilir PHP sürümü bulunamadı."
    PHP=$(whiptail --title "DEVNANOTEK — PHP" --radiolist "PHP sürümünü seçin (sonradan 'devnanotek php <sürüm>' ile ekleyip geçebilirsiniz):" 18 70 8 "${list[@]}" 3>&1 1>&2 2>&3) || die "İptal edildi."
    WEB=$(whiptail --title "DEVNANOTEK — Web sunucu" --radiolist "Web sunucu:" 12 70 2 apache "Apache (.htaccess — cPanel/Plesk ile aynı, önerilen)" ON nginx "Nginx (hafif)" OFF 3>&1 1>&2 2>&3) || die "İptal edildi."
    DB=$(whiptail --title "DEVNANOTEK — Veritabanı" --radiolist "Veritabanı:" 13 70 3 mariadb "MariaDB (MySQL uyumlu, önerilen)" ON mysql "MySQL" OFF none "Veritabanı kurma" OFF 3>&1 1>&2 2>&3) || die "İptal edildi."
    local extras
    extras=$(whiptail --title "DEVNANOTEK — Araçlar" --checklist "Kurulacak araçlar (boşluk ile seçin):" 18 74 8 \
      pma "phpMyAdmin" ON adminer "Adminer (tüm veritabanları)" ON mailpit "Mailpit (test e-posta)" ON node "Node.js $NODE_DEFAULT LTS" ON \
      composer "Composer" ON ssl "Yerel HTTPS (mkcert)" ON pg "PostgreSQL $PG_DEFAULT" OFF 3>&1 1>&2 2>&3) || die "İptal edildi."
    [[ "$extras" == *pma* ]] && PMA=1 || PMA=0
    [[ "$extras" == *adminer* ]] && ADMINER=1 || ADMINER=0
    [[ "$extras" == *mailpit* ]] && MAILPIT=1 || MAILPIT=0
    [[ "$extras" == *composer* ]] && COMPOSER=1 || COMPOSER=0
    [[ "$extras" == *node* ]] && want_node=1 || want_node=0
    [[ "$extras" == *ssl* ]] || NO_SSL=1
    [[ "$extras" == *'"pg"'* ]] && want_pg="$PG_DEFAULT"
    MODE=$(whiptail --title "DEVNANOTEK — Çalışma modu" --radiolist "Bilgisayar açılınca servisler başlasın mı?" 12 74 2 always "Her zaman açık (önerilen)" ON manual "Sadece ben başlatınca (devnanotek start)" OFF 3>&1 1>&2 2>&3) || die "İptal edildi."
  fi
  PHP="${PHP:-$PHP_DEFAULT}"
  php_installed "$PHP" || pkg_available "$(php_pkg "$PHP" fpm)" || { a="$(php_available_list | awk '{print $1}')"; warn "PHP $PHP depoda yok, $a kullanılacak."; PHP="$a"; }
  [ -n "$PHP" ] || die "Kurulabilir PHP sürümü bulunamadı."

  ensure_dirs
  self_install
  install_web || die "Web sunucu kurulamadı."
  install_php "$PHP" || die "PHP kurulamadı."
  set_cli_php "$PHP"
  install_db || warn "Veritabanı kurulumu tamamlanamadı."
  [ -n "$want_pg" ] && { install_pg "$want_pg" || warn "PostgreSQL kurulamadı."; }
  [ "$PMA" = 1 ] && [ "$DB" != none ] && { install_pma || warn "phpMyAdmin kurulamadı."; }
  [ "$ADMINER" = 1 ] && { install_adminer || warn "Adminer kurulamadı."; }
  [ "$MAILPIT" = 1 ] && { install_mailpit || { warn "Mailpit kurulamadı."; MAILPIT=0; }; }
  [ "$want_node" != 0 ] && { install_node "$([ "$want_node" = 1 ] && echo "$NODE_DEFAULT" || echo "$want_node")" || warn "Node.js kurulamadı."; }
  [ "$COMPOSER" = 1 ] && { install_composer || warn "Composer kurulamadı."; }
  [ -z "$NO_SSL" ] && { install_mkcert || warn "Yerel HTTPS kurulamadı."; }
  [ "$FAMILY" = rpm ] && selinux_setup
  write_landing
  local home; home="$(getent passwd "$DN_USER" | cut -d: -f6)"
  [ -n "$home" ] && [ ! -e "$home/httpdocs" ] && ln -s "$DN_DOCS" "$home/httpdocs" && chown -h "$DN_USER" "$home/httpdocs" && record links.txt "$home/httpdocs"
  save_state
  apply_config || warn "Yapılandırma uygulanırken hata oluştu (devnanotek status / günlük: $DN_LOG)."
  apply_mode
  echo
  ok "DEVNANOTEK kuruldu."
  echo "   Siteler    : http://localhost   (projeler: $DN_DOCS  ·  kısayol: ~/httpdocs)"
  [ "$PMA" = 1 ] && echo "   phpMyAdmin : http://localhost/phpmyadmin   (root, şifre yok)"
  [ "$ADMINER" = 1 ] && echo "   Adminer    : http://localhost/adminer"
  [ "$MAILPIT" = 1 ] && echo "   E-posta    : http://localhost:$MAILPIT_UI_PORT"
  echo "   Yönetim    : devnanotek   (menü)   ·   devnanotek help"
  echo
}

apply_mode() {
  local s
  for s in $(managed_services); do
    if [ "$MODE" = manual ]; then svc disable "$s"; else svc enable "$s"; fi
  done
}

cmd_status() {
  load_state; detect_os
  [ -f "$DN_STATE" ] || { banner; warn "DEVNANOTEK kurulu değil. Kurmak için: sudo bash devnanotek.sh install"; return 1; }
  banner
  local s name state col
  for s in $(managed_services); do
    case "$s" in
      apache2|httpd) name="Apache $(apache_version)" ;; nginx) name="Nginx $(nginx -v 2>&1 | grep -oE '[0-9.]+$')" ;;
      *fpm*) name="PHP-FPM $PHP" ;; mariadb|mysql|mysqld) name="$(db_title)" ;; postgresql*) name="PostgreSQL $PG" ;; devnanotek-mailpit) name="Mailpit" ;; *) name="$s" ;;
    esac
    if svc is-active "$s"; then state="çalışıyor"; col="$C_G"
    elif svc is-failed "$s"; then state="HATA ile durdu  (journalctl -u $s)"; col="$C_R"
    else state="durdu"; col="$C_Y"; fi
    printf '  %s●%s %-26s %s%s%s\n' "$col" "$C_0" "$name" "$col" "$state" "$C_0"
  done
  echo
  printf '  %sSürümler:%s PHP %s' "$C_D" "$C_0" "$PHP"
  [ -n "$NODE" ] && printf ' · Node.js %s' "$NODE"
  [ -d "$DN_ROOT/phpmyadmin" ] && printf ' · phpMyAdmin %s' "$(cat "$DN_ROOT/phpmyadmin/.devnanotek-version" 2>/dev/null)"
  [ -f "$DN_ROOT/adminer/version.txt" ] && printf ' · Adminer %s' "$(cat "$DN_ROOT/adminer/version.txt")"
  echo
  printf '  %sÇalışma modu:%s %s   %sKurulu PHP:%s %s\n' "$C_D" "$C_0" "$([ "$MODE" = manual ] && echo "sadece ben başlatınca" || echo "her zaman açık")" "$C_D" "$C_0" "$(php_installed_list)"
  echo "  Siteler: http://localhost   Projeler: $DN_DOCS"
  echo
}
apache_version() { if [ "$FAMILY" = deb ]; then apache2 -v 2>/dev/null; else httpd -v 2>/dev/null; fi | grep -oE 'Apache/[0-9.]+' | cut -d/ -f2; }

cmd_services() { # start|stop|restart
  need_root "$1"; load_state; detect_os
  local s fail=0
  for s in $(managed_services); do
    if svc "$1" "$s"; then ok "$1: $s"; else err "$1 başarısız: $s"; fail=1; fi
  done
  return $fail
}

cmd_php() {
  load_state; detect_os
  local v="$1"
  if [ -z "$v" ] || [ "$v" = list ]; then
    echo "Aktif   : $PHP"; echo "Kurulu  : $(php_installed_list)"; echo "Kurulabilir: $(php_available_list)"; return 0
  fi
  need_root php "$v"
  php_installed "$v" || install_php "$v" || return 1
  PHP="$v"; save_state
  set_cli_php "$v"
  apply_config && ok "Aktif PHP sürümü: $v (web ve komut satırı)."
}

ext_pkg_name() {
  case "$1" in
    mysqli|pdo_mysql|mysqlnd) echo mysql ;; pdo_pgsql|pgsql) echo pgsql ;; pdo_sqlite|sqlite3) echo sqlite3 ;;
    dom|simplexml|xmlreader|xmlwriter|xml) echo xml ;; *) echo "$1" ;;
  esac
}

cmd_ext() {
  load_state; detect_os
  local act="$1" name="$2" v="$PHP" cli; cli="$(php_cli "$v")"
  case "$act" in
    ""|list)
      echo "PHP $v yüklü eklentileri:"; "$cli" -m 2>/dev/null | grep -vE '^\[|^$' | tr '\n' ' ' | fold -s -w 100; echo; return 0 ;;
    enable|disable) [ -n "$name" ] || die "Eklenti adı yazın: devnanotek ext $act gd"; need_root ext ;;
    *) die "Kullanım: devnanotek ext [list | enable <ad> | disable <ad>]" ;;
  esac
  if [ "$act" = enable ]; then
    local pkg; pkg="$(php_pkg "$v" "$(ext_pkg_name "$name")")"
    if ! "$cli" -m 2>/dev/null | grep -qix "$name"; then
      pkg_installed "$pkg" || { pkg_available "$pkg" && pkg_install "$pkg"; } || warn "$pkg paketi depoda yok."
      if [ "$FAMILY" = deb ]; then quiet phpenmod -v "$v" -s ALL "$name"
      else local f; for f in /etc/opt/remi/php$(nodot "$v")/php.d/*"$name".ini; do [ -f "$f" ] && sed -i -E "s/^;+\s*((zend_)?extension\s*=\s*$name(\.so)?)/\1/" "$f"; done; fi
    fi
  else
    if [ "$FAMILY" = deb ]; then quiet phpdismod -v "$v" -s ALL "$name"
    else local f; for f in /etc/opt/remi/php$(nodot "$v")/php.d/*"$name".ini; do [ -f "$f" ] && sed -i -E "s/^((zend_)?extension\s*=\s*$name(\.so)?)/;\1/" "$f"; done; fi
  fi
  svc restart "$(php_fpm_svc "$v")"
  if "$cli" -m 2>/dev/null | grep -qix "$name"; then
    [ "$act" = enable ] && ok "$name açık (PHP $v)." || warn "$name hâlâ yüklü (PHP içine gömülü olabilir)."
  else
    [ "$act" = disable ] && ok "$name kapatıldı (PHP $v)." || err "$name açılamadı."
  fi
}

cmd_ini() {
  load_state; detect_os
  local key="$1" val="$2" f="$DN_ETC/php-overrides.ini"
  if [ -z "$key" ]; then
    echo "PHP $PHP ayarları (değiştirmek için: sudo devnanotek ini <ayar> <değer>):"
    "$(php_cli "$PHP")" -r 'foreach (["memory_limit","upload_max_filesize","post_max_size","max_execution_time","max_input_vars","display_errors","error_reporting","date.timezone","short_open_tag","opcache.enable"] as $k) printf("  %-22s %s\n", $k, ini_get($k));'
    [ -s "$f" ] && { echo "Sizin ayarlarınız ($f):"; sed 's/^/  /' "$f"; }
    return 0
  fi
  need_root ini
  [ -n "$val" ] || die "Değer yazın: sudo devnanotek ini $key 512M"
  mkdir -p "$DN_ETC"; touch "$f"
  grep -v -E "^[[:space:]]*${key//./\\.}[[:space:]]*=" "$f" > "$f.tmp"; echo "$key = $val" >> "$f.tmp"; mv "$f.tmp" "$f"
  local v; for v in $(php_installed_list); do write_php_ini "$v"; svc restart "$(php_fpm_svc "$v")"; done
  ok "$key = $val (tüm PHP sürümleri)."
}

cmd_vhost() {
  load_state; detect_os
  local act="$1" host="$2" dir="$3" f="$DN_ETC/vhosts.list"
  case "$act" in
    ""|list) [ -s "$f" ] && awk '{printf "  %-28s %s\n", $1, $2}' "$f" || echo "  (alan adı yok)"; return 0 ;;
    add)
      need_root vhost
      [[ "$host" =~ ^[a-z0-9][a-z0-9.-]*\.[a-z]{2,}$ ]] || die "Geçersiz alan adı: $host (örn. proje1.test)"
      [ -n "$dir" ] || dir="${host%%.*}"
      [[ "$dir" = /* ]] || dir="$DN_DOCS/$dir"
      [ -d "$dir" ] || die "Klasör yok: $dir"
      touch "$f"; awk -v h="$host" '$1!=h' "$f" > "$f.tmp"; echo "$host $dir" >> "$f.tmp"; mv "$f.tmp" "$f"
      apply_config && ok "http://$host hazır$([ "$SSL" = 1 ] && echo " (https://$host da)")." ;;
    remove|rm)
      need_root vhost
      [ -n "$host" ] || die "Alan adı yazın: sudo devnanotek vhost remove proje1.test"
      awk -v h="$host" '$1!=h' "$f" > "$f.tmp" 2>/dev/null; mv "$f.tmp" "$f"
      apply_config && ok "$host kaldırıldı." ;;
    *) die "Kullanım: devnanotek vhost [list | add <ad.test> [klasör] | remove <ad.test>]" ;;
  esac
}

cmd_db() {
  load_state; detect_os
  local act="$1" file="$2" c; c="$(db_client)"
  case "$act" in
    ""|info)
      echo "  $(db_title) · sunucu 127.0.0.1 · port $DB_PORT · kullanıcı root · şifre $([ "$DB_MANAGED" = 1 ] && echo yok || echo '(sizin)')"
      [ -n "$PG" ] && echo "  PostgreSQL $PG · sunucu 127.0.0.1 · port $PG_PORT · kullanıcı postgres · şifre gerekmez"
      return 0 ;;
    backup)
      mkdir -p "$DN_BACKUPS"
      local out="$DN_BACKUPS/$DB-$(date +%Y%m%d-%H%M%S).sql" dump; dump="$(has mariadb-dump && echo mariadb-dump || echo mysqldump)"
      "$dump" -h 127.0.0.1 -P "$DB_PORT" -uroot --all-databases --routines --events --triggers --single-transaction --default-character-set=utf8mb4 > "$out" || die "Yedek alınamadı."
      ok "Yedek: $out"
      if [ -n "$PG" ]; then
        local pout="$DN_BACKUPS/postgresql-$PG-$(date +%Y%m%d-%H%M%S).sql"
        pg_dumpall -h 127.0.0.1 -p "$PG_PORT" -U postgres --clean --if-exists -f "$pout" && ok "PostgreSQL yedeği: $pout"
      fi ;;
    restore)
      [ -f "$file" ] || die "Dosya yok: $file"
      if head -c 4000 "$file" | grep -q "PostgreSQL database cluster dump"; then psql -h 127.0.0.1 -p "$PG_PORT" -U postgres -d postgres -f "$file" >/dev/null || die "Geri yüklenemedi."
      else "$c" -h 127.0.0.1 -P "$DB_PORT" -uroot --default-character-set=utf8mb4 < "$file" || die "Geri yüklenemedi."; fi
      ok "Geri yüklendi: $file" ;;
    create)
      [ -n "$file" ] || die "Ad yazın: devnanotek db create veritabanim"
      "$c" -h 127.0.0.1 -P "$DB_PORT" -uroot -e "CREATE DATABASE IF NOT EXISTS \`$file\` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci" && ok "Oluşturuldu: $file" ;;
    *) die "Kullanım: devnanotek db [info | backup | restore <dosya.sql> | create <ad>]" ;;
  esac
}

cmd_postgres() {
  load_state; detect_os
  case "$1" in
    ""|info) [ -n "$PG" ] && echo "PostgreSQL $PG · port $PG_PORT · kullanıcı postgres (şifresiz, yalnız bu bilgisayardan)" || echo "PostgreSQL kurulu değil: sudo devnanotek postgres install [sürüm]"; return 0 ;;
    install) need_root postgres; install_pg "${2:-$PG_DEFAULT}" && save_state && apply_mode ;;
    *) die "Kullanım: devnanotek postgres [info | install [14-18]]" ;;
  esac
}

cmd_mode() {
  need_root mode; load_state; detect_os
  case "$1" in always|manual) MODE="$1" ;; *) die "Kullanım: sudo devnanotek mode always | manual" ;; esac
  save_state; apply_mode
  [ "$MODE" = always ] && ok "Servisler bilgisayar açılınca başlayacak." || ok "Servisler yalnız siz 'devnanotek start' deyince çalışacak."
}

cmd_node() {
  load_state; detect_os
  [ -n "$1" ] || { echo "Node.js: ${NODE:-kurulu değil}  (değiştirmek: sudo devnanotek node 22)"; ls "$DN_ROOT/node" 2>/dev/null | grep -v current | sed 's/^/  kurulu: /'; return 0; }
  need_root node; install_node "$1" && save_state
}

cmd_uninstall() {
  need_root uninstall; load_state; detect_os
  local all=0; [ "$1" = --all ] && all=1
  banner
  echo "DEVNANOTEK kaldırılacak: servis ayarları, web sunucu yapılandırması, hosts kayıtları, sertifikalar, kısayollar."
  [ "$all" = 1 ] && echo "${C_R}--all: $DN_DOCS (PROJELERİNİZ) ve $DN_BACKUPS de silinecek!${C_0}" || echo "Projeleriniz ($DN_DOCS) ve yedekler korunur."
  confirm "Devam edilsin mi?" h || return 1
  if [ "$all" = 1 ] && [ "$DB" != none ] && confirm "Silmeden önce tüm veritabanlarının yedeği ana klasörünüze alınsın mı?" e; then
    local home; home="$(getent passwd "$DN_USER" | cut -d: -f6)"; local out="${home:-/root}/devnanotek-veritabani-yedegi-$(date +%Y%m%d-%H%M%S).sql"
    { has mariadb-dump && mariadb-dump -h 127.0.0.1 -P "$DB_PORT" -uroot --all-databases > "$out"; } || mysqldump -h 127.0.0.1 -P "$DB_PORT" -uroot --all-databases > "$out" && ok "Yedek: $out"
  fi
  local v s
  [ "$HAS_SYSTEMD" = 1 ] && { svc stop devnanotek-mailpit; svc disable devnanotek-mailpit; rm -f /etc/systemd/system/devnanotek-mailpit.service; quiet systemctl daemon-reload; }
  for v in $PHP_CANDIDATES; do rm -f "$(php_pool_dir "$v")/devnanotek.conf" $(php_ini_files "$v") 2>/dev/null; done
  rm -f "$NGINX_CONF"
  if [ "$FAMILY" = deb ]; then
    quiet a2dissite devnanotek; rm -f "$APACHE_CONF"
    [ "$DEFAULT_SITE_DISABLED" = 1 ] && quiet a2ensite 000-default
  else rm -f "$APACHE_CONF"; fi
  rm -f /etc/mysql/mariadb.conf.d/99-devnanotek.cnf /etc/mysql/mysql.conf.d/99-devnanotek.cnf /etc/my.cnf.d/99-devnanotek.cnf
  rm -f "$DN_ETC/vhosts.list"; write_hosts
  has mkcert && [ -d "$DN_SSL/ca" ] && quiet env CAROOT="$DN_SSL/ca" mkcert -uninstall
  if [ "$SELINUX_DONE" = 1 ] && has semanage; then
    local p; for p in httpdocs tmp logs mailpit phpmyadmin adminer ssl; do quiet semanage fcontext -d "$DN_ROOT/$p(/.*)?"; done
  fi
  # paketler: yalnız DEVNANOTEK'in kurduğu paketler, isteğe bağlı
  if [ -s "$DN_ETC/installed-packages.txt" ]; then
    echo "DEVNANOTEK'in kurduğu paketler: $(tr '\n' ' ' < "$DN_ETC/installed-packages.txt" | cut -c1-300)…"
    if confirm "Bu paketler de kaldırılsın mı? (veritabanı dosyaları /var/lib altında kalır)" h; then
      local pk=(); while read -r s; do [ -n "$s" ] && pkg_installed "$s" && pk+=("$s"); done < "$DN_ETC/installed-packages.txt"
      if [ ${#pk[@]} -gt 0 ]; then
        if [ "$FAMILY" = deb ]; then run env DEBIAN_FRONTEND=noninteractive apt-get remove -y "${pk[@]}"; quiet apt-get autoremove -y
        else run dnf remove -y "${pk[@]}"; fi
      fi
      if [ -s "$DN_ETC/added-repos.txt" ]; then
        while read -r s; do
          case "$s" in ppa:*) quiet add-apt-repository -y --remove "$s" ;; /*) rm -f "$s" ;; esac
        done < "$DN_ETC/added-repos.txt"
      fi
    else
      for s in $(managed_services); do svc restart "$s"; done
    fi
  fi
  if [ -s "$DN_ETC/links.txt" ]; then while read -r s; do [ -L "$s" ] || [ "$s" = /usr/local/bin/mkcert ] || [ "$s" = /usr/local/bin/composer ] || [ "$s" = /usr/local/bin/mailpit ] && rm -f "$s"; done < "$DN_ETC/links.txt"; fi
  rm -f "$DN_LINK"
  if [ "$all" = 1 ]; then rm -rf "$DN_ROOT"
  else
    find "$DN_ROOT" -mindepth 1 -maxdepth 1 ! -name httpdocs ! -name backups -exec rm -rf {} + 2>/dev/null
  fi
  ok "DEVNANOTEK kaldırıldı.$([ "$all" = 1 ] || echo " Projeleriniz: $DN_DOCS")"
}

cmd_menu() {
  load_state; detect_os
  [ -f "$DN_STATE" ] || { cmd_status; return; }
  local choice
  while true; do
    if has whiptail && [ -t 0 ]; then
      choice=$(whiptail --title "DEVNANOTEK Local Server $DN_VERSION" --menu "PHP $PHP · $WEB · $DB$([ -n "$PG" ] && echo " · PostgreSQL $PG")" 22 70 13 \
        1 "Durum" 2 "Tümünü başlat" 3 "Tümünü durdur" 4 "Yeniden başlat" 5 "PHP sürümü değiştir" 6 "PHP eklentisi aç / kapat" \
        7 "php.ini ayarı" 8 "Alan adı ekle (proje.test)" 9 "Veritabanı yedeği al" 10 "Çalışma modu" 11 "Yapılandırmayı yeniden uygula" 12 "Kaldır" 0 "Çıkış" 3>&1 1>&2 2>&3) || return 0
    else
      echo "1) Durum 2) Başlat 3) Durdur 4) Yeniden başlat 5) PHP sürümü 6) Eklenti 7) php.ini 8) Alan adı 9) Yedek 10) Mod 11) Uygula 12) Kaldır 0) Çıkış"
      read -r -p "Seçim: " choice
    fi
    local S=""; [ "$(id -u)" -eq 0 ] || S="sudo"
    case "$choice" in
      1) cmd_status; read -r -p "Devam için Enter..." _ ;;
      2) $S "$DN_LINK" start ;; 3) $S "$DN_LINK" stop ;; 4) $S "$DN_LINK" restart ;;
      5) local v; v=$(whiptail --inputbox "PHP sürümü (kurulabilir: $(php_available_list)):" 10 70 "$PHP" 3>&1 1>&2 2>&3) && $S "$DN_LINK" php "$v" ;;
      6) local e a; e=$(whiptail --inputbox "Eklenti adı (ör. gd, zip, intl, redis):" 10 60 3>&1 1>&2 2>&3) && \
         a=$(whiptail --menu "$e" 12 50 2 enable "Aç" disable "Kapat" 3>&1 1>&2 2>&3) && $S "$DN_LINK" ext "$a" "$e" ;;
      7) local k val; k=$(whiptail --inputbox "Ayar adı (ör. memory_limit):" 10 60 3>&1 1>&2 2>&3) && val=$(whiptail --inputbox "$k değeri:" 10 60 3>&1 1>&2 2>&3) && $S "$DN_LINK" ini "$k" "$val" ;;
      8) local h d; h=$(whiptail --inputbox "Alan adı (ör. proje1.test):" 10 60 3>&1 1>&2 2>&3) && d=$(whiptail --inputbox "httpdocs içindeki klasör (ör. GITHUB/proje1):" 10 70 3>&1 1>&2 2>&3) && $S "$DN_LINK" vhost add "$h" "$d" ;;
      9) $S "$DN_LINK" db backup ;;
      10) local m; m=$(whiptail --menu "Çalışma modu" 12 70 2 always "Her zaman açık" manual "Sadece ben başlatınca" 3>&1 1>&2 2>&3) && $S "$DN_LINK" mode "$m" ;;
      11) $S "$DN_LINK" apply ;;
      12) $S "$DN_LINK" uninstall; return 0 ;;
      0|q|"") return 0 ;;
    esac
    has whiptail && [ -t 0 ] && [ "$choice" != 1 ] && read -r -p "Devam için Enter..." _
  done
}

cmd_help() {
  banner
  cat <<EOF
  Kurulum
    sudo bash devnanotek.sh install          sihirbazla kur (veya seçeneklerle:)
         --php 8.4 --web apache|nginx --db mariadb|mysql|none [--db-version 11.4]
         --pg 17 --node 24 --no-node --no-mail --no-ssl --mode always|manual -y

  Günlük kullanım
    devnanotek                               menü
    devnanotek status                        servisler ve sürümler (yeşil/sarı/kırmızı)
    sudo devnanotek start | stop | restart
    sudo devnanotek apply                    yapılandırmayı yeniden yaz ve uygula

  PHP
    devnanotek php                           kurulu / kurulabilir sürümler
    sudo devnanotek php 8.3                  sürüm kur ve geç (web + komut satırı)
    devnanotek ext                           yüklü eklentiler
    sudo devnanotek ext enable gd            eklenti aç (gd, zip, intl, redis, imagick…)
    sudo devnanotek ext disable xdebug       eklenti kapat
    devnanotek ini                           önemli php.ini ayarları
    sudo devnanotek ini memory_limit 1G      php.ini ayarı değiştir

  Alan adları
    devnanotek vhost                         liste
    sudo devnanotek vhost add proje1.test GITHUB/proje1
    sudo devnanotek vhost remove proje1.test

  Veritabanı
    devnanotek db                            bağlantı bilgileri
    sudo devnanotek db backup                tüm veritabanlarını yedekle ($DN_BACKUPS)
    sudo devnanotek db restore dosya.sql
    devnanotek db create veritabanim
    sudo devnanotek postgres install 17      PostgreSQL ekle (14-18)

  Diğer
    sudo devnanotek node 22                  Node.js ana sürümü kur/geç
    sudo devnanotek mode always | manual     açılışta başlasın mı
    sudo devnanotek uninstall [--all]        kaldır (--all: projeler dahil)

  Projeler: $DN_DOCS  →  http://localhost/klasor
EOF
}

# ================================================================ ANA ======
main() {
  local c="${1:-menu}"; shift 2>/dev/null
  case "$c" in
    install) cmd_install "$@" ;;
    status|durum) cmd_status ;;
    start|stop|restart) cmd_services "$c" ;;
    apply) need_root apply; load_state; detect_os; apply_config ;;
    php) cmd_php "$@" ;;
    ext) cmd_ext "$@" ;;
    ini) cmd_ini "$@" ;;
    vhost) cmd_vhost "$@" ;;
    db) cmd_db "$@" ;;
    postgres|pg) cmd_postgres "$@" ;;
    node) cmd_node "$@" ;;
    mode) cmd_mode "$@" ;;
    uninstall|remove) cmd_uninstall "$@" ;;
    menu) cmd_menu ;;
    version|--version|-v) echo "DEVNANOTEK Local Server $DN_VERSION (Linux)" ;;
    help|--help|-h) cmd_help ;;
    *) err "Bilinmeyen komut: $c"; cmd_help; return 1 ;;
  esac
}

main "$@"
