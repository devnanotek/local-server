<?php
/**
 * DEVNANOTEK — phpMyAdmin yapılandırması
 * BU DOSYA OTOMATİK ÜRETİLİR ("Uygula" ile yeniden yazılır).
 * Kendi eklemeleriniz için aynı klasörde "config.devnanotek.custom.php" dosyası oluşturun.
 */
declare(strict_types=1);

// Yeni PHP sürümlerindeki "deprecated" bildirimleri phpMyAdmin sayfalarını bozmasın
error_reporting(E_ALL & ~E_DEPRECATED & ~E_USER_DEPRECATED & ~E_NOTICE);
@ini_set('display_errors', '0');

$cfg['blowfish_secret'] = '{{SECRET}}';

$i = 0;
$i++;
$cfg['Servers'][$i]['verbose']          = '{{SERVER_LABEL}}';
$cfg['Servers'][$i]['auth_type']        = '{{AUTH_TYPE}}';
$cfg['Servers'][$i]['host']             = '127.0.0.1';
$cfg['Servers'][$i]['port']             = '{{PORT}}';
$cfg['Servers'][$i]['compress']         = false;
$cfg['Servers'][$i]['AllowNoPassword']  = true;
$cfg['Servers'][$i]['user']             = '{{USER}}';
$cfg['Servers'][$i]['password']         = '{{PASS}}';

$cfg['UploadDir']          = '';
$cfg['SaveDir']            = '';
$cfg['TempDir']            = '{{TEMP_DIR}}';
$cfg['MaxRows']            = 50;
$cfg['SendErrorReports']   = 'never';
$cfg['ShowPhpInfo']        = true;
$cfg['ShowServerInfo']     = true;
$cfg['DefaultLang']        = 'tr';
$cfg['LoginCookieValidity']= 86400 * 7;
$cfg['ExecTimeLimit']      = 0;
$cfg['MemoryLimit']        = '1024M';
$cfg['VersionCheck']       = false;
$cfg['ShowCreateDb']       = true;
$cfg['NavigationTreeEnableGrouping'] = false;
$cfg['DefaultCharset']     = 'utf8mb4';

$__custom = __DIR__ . DIRECTORY_SEPARATOR . 'config.devnanotek.custom.php';
if (is_file($__custom)) {
    include $__custom;
}
