using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

static class DynamicPatcher
{
    static string EscapeJs(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c > 0x7f)
                sb.Append("\\u").Append(((int)c).ToString("x4"));
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    static string ReplaceFirst(string src, string oldVal, string newVal)
    {
        int idx = src.IndexOf(oldVal, StringComparison.Ordinal);
        if (idx < 0) return src;
        return src.Substring(0, idx) + newVal + src.Substring(idx + oldVal.Length);
    }

    static string StripRegion(string text)
    {
        return Regex.Replace(text, @"\r?\n?//#region custom-font-mod[\s\S]*?//#endregion custom-font-mod\r?\n?", "");
    }

    static string StripHtmlBlock(string html)
    {
        html = Regex.Replace(html, @"<!-- custom-font-mod-start -->[\s\S]*?<!-- custom-font-mod-end -->\r?\n?", "");
        html = Regex.Replace(html, @"<style id=""custom-font-mod-fonts"">[\s\S]*?</script>\r?\n?", "");
        return html;
    }

    static string ReadAsarText(FileStream fs, long baseDataOffset, IDictionary<string, object> node)
    {
        long off = Convert.ToInt64(node["offset"]);
        int sz = Convert.ToInt32(node["size"]);
        byte[] buf = new byte[sz];
        fs.Seek(baseDataOffset + off, SeekOrigin.Begin);
        int read = 0;
        while (read < sz)
        {
            int n = fs.Read(buf, read, sz - read);
            if (n <= 0) break;
            read += n;
        }
        return Encoding.UTF8.GetString(buf, 0, read);
    }

    static IDictionary<string, object> FindNode(IDictionary<string, object> root, string relPath)
    {
        IDictionary<string, object> cur = root;
        foreach (string part in relPath.Split('/'))
        {
            if (!cur.ContainsKey("files")) return null;
            var files = cur["files"] as IDictionary<string, object>;
            if (files == null || !files.ContainsKey(part)) return null;
            cur = files[part] as IDictionary<string, object>;
            if (cur == null) return null;
        }
        return cur;
    }

    static void FindAllPaths(IDictionary<string, object> node, string prefix, List<string> outList)
    {
        if (node.ContainsKey("files") && node["files"] is IDictionary<string, object>)
        {
            var files = (IDictionary<string, object>)node["files"];
            foreach (var kv in files)
            {
                string p = String.IsNullOrEmpty(prefix) ? kv.Key : (prefix + "/" + kv.Key);
                var child = kv.Value as IDictionary<string, object>;
                if (child != null) FindAllPaths(child, p, outList);
            }
        }
        else
        {
            outList.Add(prefix);
        }
    }

    public static Dictionary<string, byte[]> BuildDynamicOverrides(
        FileStream srcFs,
        long baseDataOffset,
        IDictionary<string, object> headerRoot,
        Dictionary<string, ZipArchiveEntry> metaEntries,
        JavaScriptSerializer json)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var allPaths = new List<string>();
        FindAllPaths(headerRoot, "", allPaths);

        Func<string, string> readMeta = (name) =>
        {
            ZipArchiveEntry e;
            if (!metaEntries.TryGetValue(name, out e))
                throw new IOException("ไม่พบไฟล์ meta: " + name);
            using (var sr = new StreamReader(e.Open(), Encoding.UTF8))
                return sr.ReadToEnd();
        };

        // 1. main.js & preload.js
        var mainNode = FindNode(headerRoot, "main.js");
        if (mainNode != null)
        {
            string mainJs = StripRegion(ReadAsarText(srcFs, baseDataOffset, mainNode)).TrimEnd() + "\n" + readMeta("main_region.js");
            result["main.js"] = Encoding.UTF8.GetBytes(mainJs);
        }

        var preloadNode = FindNode(headerRoot, "preload.js");
        if (preloadNode != null)
        {
            string preloadJs = StripRegion(ReadAsarText(srcFs, baseDataOffset, preloadNode)).TrimEnd() + "\n" + readMeta("preload_region.js");
            result["preload.js"] = Encoding.UTF8.GetBytes(preloadJs);
        }

        // 2. assets/web-mobile/index.html (non-blocking boot)
        string htmlPath = "assets/web-mobile/index.html";
        var htmlNode = FindNode(headerRoot, htmlPath);
        if (htmlNode != null)
        {
            string html = StripHtmlBlock(ReadAsarText(srcFs, baseDataOffset, htmlNode));
            html = html.Replace(
                "document.fonts.load('400 24px GoogleSansThai').then(()=>window.boot(),()=>window.boot());",
                "window.boot();"
            );
            html = ReplaceFirst(html, "</head>", readMeta("index_head.html") + "\n</head>");
            result[htmlPath] = Encoding.UTF8.GetBytes(html);
        }

        // 3. assets/web-mobile/src/assets/script/libs/fairygui.<hash>.js (wildcard)
        string fguiPath = allPaths.FirstOrDefault(p => Regex.IsMatch(p, @"^assets/web-mobile/src/assets/script/libs/fairygui\.[0-9a-f]+\.js$"));
        if (fguiPath != null)
        {
            string fgui = ReadAsarText(srcFs, baseDataOffset, FindNode(headerRoot, fguiPath));
            if (!fgui.Contains("window.__cfActiveFont"))
            {
                fgui = ReplaceFirst(fgui, "e.defaultFont=\"Arial\"", "e.defaultFont=(window.__cfActiveFont||\"GoogleSansThai\")");
                fgui = ReplaceFirst(
                    fgui,
                    "i.prototype.assignFont=function(e,i){if(i instanceof cc.Font)e.font=i;else{var n=t.getFontByName(i);n?e.font=n:(e.fontFamily=i,e.useSystemFont=!0)}}",
                    "i.prototype.assignFont=function(e,i){if(i instanceof cc.Font)e.font=i;else{var cf=window.__cfActiveFont;if(cf&&cf!==\"Arial\"){e.font=null;e.fontFamily=cf+',\"Microsoft YaHei\",\"Meiryo\",\"Malgun Gothic\",\"Leelawadee UI\",sans-serif';e.useSystemFont=!0;return}var n=t.getFontByName(i);n?e.font=n:(e.fontFamily=i,e.useSystemFont=!0)}}"
                );
                fgui = ReplaceFirst(
                    fgui,
                    "else this._label.fontSize=this._fontSize,this._label.lineHeight=this._fontSize+this._leading;this._cachedLineHeight=0",
                    "else{var _isSub=!!(this.__isSubtitle||\"subtitle\"===this.name||\"subtitle_r\"===this.name),_sc=(_isSub?window.__cfSubScale:window.__cfUiScale)||1,_fs=Math.round(this._fontSize*_sc),_lh=_fs+Math.round(this._leading*_sc);if(window.__isThaiLang&&window.__isThaiLang()){_lh=Math.max(_lh,Math.round(_fs*1.32));}this._label.fontSize=_fs,this._label.lineHeight=_lh}this._cachedLineHeight=0"
                );
                fgui = ReplaceFirst(
                    fgui,
                    "var t=this._fontSize,e=this._richText.font;e instanceof cc.BitmapFont&&(e._fntConfig.resizable||(t=e._fntConfig.fontSize)),this._richText.fontSize=t,this._richText.lineHeight=t+2*this._leading",
                    "var t=this._fontSize,e=this._richText.font,_isSub=!!(this.__isSubtitle||\"subtitle\"===this.name||\"subtitle_r\"===this.name),_sc=(_isSub?window.__cfSubScale:window.__cfUiScale)||1;e instanceof cc.BitmapFont?(e._fntConfig.resizable||(t=e._fntConfig.fontSize)):t=Math.round(t*_sc),this._richText.fontSize=t,this._richText.lineHeight=t+2*Math.round(this._leading*_sc)"
                );
                fgui = ReplaceFirst(
                    fgui,
                    "i.prototype.updateText=function(){if(!this._isUpdatingText&&null!=this.node){this._isUpdatingText=!0;var e=this._text;",
                    "i.prototype.updateText=function(){if(!this._isUpdatingText&&null!=this.node){this._isUpdatingText=!0;var e=window.__thFguiTranslate?window.__thFguiTranslate(this._text):this._text;"
                );
                fgui = ReplaceFirst(
                    fgui,
                    "n.prototype.updateText=function(){var e=this._text;",
                    "n.prototype.updateText=function(){var e=window.__thFguiTranslate?window.__thFguiTranslate(this._text):this._text;"
                );
            }
            result[fguiPath] = Encoding.UTF8.GetBytes(fgui);
        }

        // 4. assets/web-mobile/assets/main/index.<hash>.js (wildcard)
        string indexJsPath = allPaths.FirstOrDefault(p => Regex.IsMatch(p, @"^assets/web-mobile/assets/main/index\.[0-9a-f]+\.js$"));
        if (indexJsPath != null)
        {
            string src = ReadAsarText(srcFs, baseDataOffset, FindNode(headerRoot, indexJsPath));
            if (src.Contains("I18nManager.prototype.getGameText = function(key, scane, lang) {"))
            {
                if (!src.Contains("/* undertone-readable-hooks */"))
                {
                    src = ReplaceFirst(src, "I18nType[\"ZH\"] = \"zh\";", "I18nType[\"ZH\"] = \"zh\"; I18nType.TH = \"th\";");
                    src = ReplaceFirst(src, "I18nManager.prototype.init = function() {",
                        "I18nManager.prototype.init = function() {/* undertone-readable-hooks */ window.__undertoneI18n=this; var dl=cc.assetManager&&cc.assetManager.downloader;if(dl&&!dl.__thPatched){dl.__thPatched=true;var originalDownload=dl.download;dl.download=function(){var args=Array.prototype.slice.call(arguments);if(typeof args[1]===\"string\"&&window.__remapThaiImageUrl)args[1]=window.__remapThaiImageUrl(args[1]);return originalDownload.apply(this,args);};} try { var saved=localStorage.getItem(\"undertone_lang\"); if(saved){this.language=saved;return;} } catch(e){}");
                    src = ReplaceFirst(src, "setString(\"settingLanguage\", language);",
                        "setString(\"settingLanguage\", language); try {localStorage.setItem(\"undertone_lang\", language);} catch(e){}");
                    src = ReplaceFirst(src, "I18nManager.prototype.getGameText = function(key, scane, lang) {",
                        "I18nManager.prototype.getGameText = function(key, scane, lang) {if((lang||this.language)===\"th\"){var scope=scane||GameInitze_1.gameStoryModel.nowGameKey||\"main\";var thai=window.__thLangLookup&&window.__thLangLookup(scope,key);if(thai!=null)return thai;lang=\"zh\";}");
                    src = ReplaceFirst(src, "getLangName = function(lang) {",
                        "getLangName = function(lang) {if(lang===\"th\")return \"ไทย\";");
                    src = Regex.Replace(src, @"(this\.langList\.itemRenderer\s*=\s*function\(index, item\)\s*\{\s*item\.data\s*=\s*(?:list_1\[index\]|index);\s*item\.title\s*=\s*su(?:_1)?\.getLangName\(list_1\[index\]\);)",
                        "if(list_1.indexOf(\"th\")<0)list_1.push(\"th\"); $1");
                    src = ReplaceFirst(src, "this._supLang.set(gameId, list);",
                        "if(gameId===\"ds\"&&list.indexOf(\"th\")<0)list.push(\"th\"); this._supLang.set(gameId, list);");
                    src = ReplaceFirst(src, "GameInitze.prototype.loadSubtitleJson = function(gameId) {",
                        "GameInitze.prototype.loadSubtitleJson = function(gameId) {if(gameId===\"ds\"&&window.__isThaiLang&&window.__isThaiLang()&&window.__thData&&window.__thData.subtitles){this._subtitleCache.set(\"ds_th\",window.__thData.subtitles);return;}");
                    src = ReplaceFirst(src, "GameInitze.prototype.getSubtitleJson = function(gameId) {",
                        "GameInitze.prototype.getSubtitleJson = function(gameId) {if(gameId===\"ds\"&&window.__isThaiLang&&window.__isThaiLang()&&window.__thData&&window.__thData.subtitles)return window.__thData.subtitles;");
                    src = ReplaceFirst(src, "return storyPoint._subtitleName;",
                        "return storyPoint._subtitleName||(window.__isThaiLang&&window.__isThaiLang()&&storyPoint._videoPath?storyPoint._videoPath.replace(/\\.mp4$/i,\"\"):\"\");");
                    src = Regex.Replace(src, @"(case\s+""ExitGame""\s*:\s*)App_1\.default\.GameStateManager\.exitGame\(\);",
                        "$1try { App_1.default.GameStateManager.exitGame(); } finally { window.close(); }");
                    src = ReplaceFirst(src, "GameSetting.prototype.onShown = function() {",
                        "GameSetting.prototype.onShown = function() {window.__cfSetSettingOpen&&window.__cfSetSettingOpen(true);");
                    src = ReplaceFirst(src, "GameSetting.prototype.clearSth = function() {",
                        "GameSetting.prototype.clearSth = function() {window.__cfSetSettingOpen&&window.__cfSetSettingOpen(false);");
                }
                if (!src.Contains("finally { window.close(); }") || !src.Contains("this._subtitleCache.set(\"ds_th\","))
                    throw new InvalidOperationException("Could not apply the current build's exit and Thai subtitle hooks.");
                result[indexJsPath] = Encoding.UTF8.GetBytes(src);
                return result;
            }
            if (!src.Contains("window.__undertoneI18n=this;"))
            {
                src = ReplaceFirst(
                    src,
                    "e.ZH=\"zh\",e.EN=\"en\",e.JP=\"ja\",e.KR=\"ko\",e.ZHTW=\"zh-tw\",e.RU=\"ru\",e.SP=\"sp\",e.FR=\"fr\"",
                    "e.ZH=\"zh\",e.EN=\"en\",e.JP=\"ja\",e.KR=\"ko\",e.ZHTW=\"zh-tw\",e.RU=\"ru\",e.SP=\"sp\",e.FR=\"fr\",e.TH=\"th\""
                );
                src = ReplaceFirst(
                    src,
                    "t.prototype.init=function(){var e;if(this.refreshData(),null===(e=l.default.PlatformManager.sysData)||void 0===e?void 0:e.language){var t=l.default.PlatformManager.sysData.language.toLowerCase();t.startsWith(\"zh\")?this.language=\"zh-hant-tw\"===t||\"zh-hant-hk\"===t||\"zh-tw\"===t?u.I18nType.ZHTW:u.I18nType.ZH:t.startsWith(\"en\")?this.language=u.I18nType.EN:t.startsWith(\"ja\")?this.language=u.I18nType.JP:t.startsWith(\"ko\")?this.language=u.I18nType.KR:t.startsWith(\"ru\")?this.language=u.I18nType.RU:i.default.IsInternaton?this.language=u.I18nType.EN:this.language=u.I18nType.ZH}}",
                    "t.prototype.init=function(){window.__undertoneI18n=this;if(window.cc&&cc.assetManager&&cc.assetManager.downloader&&!cc.assetManager.downloader.__thPatched){cc.assetManager.downloader.__thPatched=!0;var _odl=cc.assetManager.downloader.download;cc.assetManager.downloader.download=function(a,b,c,d,e){if(\"string\"==typeof b&&window.__remapThaiImageUrl)b=window.__remapThaiImageUrl(b);return _odl.call(this,a,b,c,d,e)}}this.refreshData();var saved=null;try{saved=localStorage.getItem(\"undertone_lang\")}catch(x){}if(saved){this.language=saved;return}var e;if(null===(e=l.default.PlatformManager.sysData)||void 0===e?void 0:e.language){var t=l.default.PlatformManager.sysData.language.toLowerCase();t.startsWith(\"zh\")?this.language=\"zh-hant-tw\"===t||\"zh-hant-hk\"===t||\"zh-tw\"===t?u.I18nType.ZHTW:u.I18nType.ZH:t.startsWith(\"en\")?this.language=u.I18nType.EN:t.startsWith(\"ja\")?this.language=u.I18nType.JP:t.startsWith(\"ko\")?this.language=u.I18nType.KR:t.startsWith(\"ru\")?this.language=u.I18nType.RU:i.default.IsInternaton?this.language=u.I18nType.EN:this.language=u.I18nType.ZH}}"
                );
                src = ReplaceFirst(
                    src,
                    "l.default.LocalStorageDataManager.setString(\"settingLanguage\",e),this._language=e",
                    "l.default.LocalStorageDataManager.setString(\"settingLanguage\",e),this._language=e,(function(){try{localStorage.setItem(\"undertone_lang\",e)}catch(x){}})()"
                );
                src = ReplaceFirst(
                    src,
                    "n||(n=this._language);var o=\"\";return n===u.I18nType.ZH?o=r.Chinese:",
                    "n||(n=this._language);if(n===u.I18nType.TH&&window.__thLangLookup){var thv=window.__thLangLookup(t,e);if(thv)return thv.replace(/\\\\n/g,\"\\n\");}var o=\"\";return n===u.I18nType.ZH?o=r.Chinese:"
                );
                src = ReplaceFirst(
                    src,
                    "case l.I18nType.ZHTW:return\"\\u7e41\\u9ad4\\u4e2d\\u6587\";default:return e",
                    "case l.I18nType.ZHTW:return\"\\u7e41\\u9ad4\\u4e2d\\u6587\";case l.I18nType.TH:return\"\\u0e44\\u0e17\\u0e22\";default:return e"
                );
                src = ReplaceFirst(
                    src,
                    "var n=[l.I18nType.ZH,l.I18nType.ZHTW,l.I18nType.EN,l.I18nType.JP,l.I18nType.KR];",
                    "var n=[l.I18nType.ZH,l.I18nType.ZHTW,l.I18nType.EN,l.I18nType.JP,l.I18nType.KR,l.I18nType.TH];"
                );
                src = ReplaceFirst(
                    src,
                    "for(var _ in o)o[_]==m&&d.push(_)}",
                    "for(var _ in o)o[_]==m&&d.push(_)}d.indexOf(c.I18nType.TH)===-1&&d.push(c.I18nType.TH);"
                );
                src = src.Replace(
                    "s.default.EventManager.emitEvent(c.ViewEvent.LanguageChange)",
                    "(window.__thRefreshAllFgui&&window.__thRefreshAllFgui()),s.default.EventManager.emitEvent(c.ViewEvent.LanguageChange)"
                );
                src = src.Replace(
                    "s.default.EventManager.emitEvent(l.ViewEvent.LanguageChange)",
                    "(window.__thRefreshAllFgui&&window.__thRefreshAllFgui()),s.default.EventManager.emitEvent(l.ViewEvent.LanguageChange)"
                );

                // GameSetting onShown / clearSth
                int gsPos = src.IndexOf("GameSetting:[function(", StringComparison.Ordinal);
                if (gsPos >= 0)
                {
                    int onShown = src.IndexOf("t.prototype.onShown=function(){", gsPos, StringComparison.Ordinal);
                    if (onShown >= 0)
                        src = src.Insert(onShown + "t.prototype.onShown=function(){".Length, "window.__cfSetSettingOpen&&window.__cfSetSettingOpen(!0);");
                    int clearSth = src.IndexOf("t.prototype.clearSth=function(){", gsPos, StringComparison.Ordinal);
                    if (clearSth >= 0)
                        src = src.Insert(clearSth + "t.prototype.clearSth=function(){".Length, "window.__cfSetSettingOpen&&window.__cfSetSettingOpen(!1);");
                }

                // SettingView onShown / clearSth / p_updPage1..3
                int svPos = src.IndexOf("SettingView:[function(", StringComparison.Ordinal);
                if (svPos >= 0)
                {
                    int onShown = src.IndexOf("t.prototype.onShown=function(){", svPos, StringComparison.Ordinal);
                    if (onShown >= 0)
                        src = src.Insert(onShown + "t.prototype.onShown=function(){".Length, "window.__activeSettingView=this;");
                    int clearSth = src.IndexOf("t.prototype.clearSth=function(){", svPos, StringComparison.Ordinal);
                    if (clearSth >= 0)
                        src = src.Insert(clearSth + "t.prototype.clearSth=function(){".Length, "window.__activeSettingView=null;window.__cfSetSettingOpen&&window.__cfSetSettingOpen(!1);");
                    foreach (var pair in new[] { Tuple.Create("p_updPage1", "!1"), Tuple.Create("p_updPage2", "!0"), Tuple.Create("p_updPage3", "!1") })
                    {
                        string hdr = "t.prototype." + pair.Item1 + "=function(){";
                        int idx = src.IndexOf(hdr, svPos, StringComparison.Ordinal);
                        if (idx >= 0)
                            src = src.Insert(idx + hdr.Length, "window.__cfSetSettingOpen&&window.__cfSetSettingOpen(" + pair.Item2 + ");");
                    }
                }

                src = ReplaceFirst(src, "t._chapterId+\"_\"+ +t._stageLink/100+\"_\"+n+t._subtitleName}return t._subtitleName}", "t._chapterId+\"_\"+ +t._stageLink/100+\"_\"+n+t._subtitleName}return t._subtitleName||(window.__isThaiLang&&window.__isThaiLang()&&t._videoPath?t._videoPath.replace(/\\.mp4$/i,\"\"):\"\")}");
                src = ReplaceFirst(src, "return this._supLang.set(e,a),a}", "return\"ds\"===e&&a.indexOf(y.I18nType.TH)===-1&&a.push(y.I18nType.TH),this._supLang.set(e,a),a}");
                src = ReplaceFirst(
                    src,
                    "t.prototype.loadSubtitleJson=function(e){var t=this,n=e+\"_\"+this.getSubLang(e);this._subtitleCache.get(n)||",
                    "t.prototype.loadSubtitleJson=function(e){var t=this,n=e+\"_\"+this.getSubLang(e);if(\"ds_th\"===n&&window.__thData&&window.__thData.subtitles){this._subtitleCache.set(n,window.__thData.subtitles);return}this._subtitleCache.get(n)||"
                );
                src = ReplaceFirst(
                    src,
                    "t.prototype.getSubtitleJson=function(e){var t=e+\"_\"+s.default.I18nManager.language,n=this._subtitleCache.get(t);if(n)return n;",
                    "t.prototype.getSubtitleJson=function(e){var t=e+\"_\"+s.default.I18nManager.language;if(\"ds_th\"===t&&window.__thData&&window.__thData.subtitles){this._subtitleCache.set(t,window.__thData.subtitles);return window.__thData.subtitles}var n=this._subtitleCache.get(t);if(n)return n;"
                );
                src = ReplaceFirst(src, "fgui.UIConfig.defaultFont=\"\\u5fae\\u8f6f\\u96c5\\u9ed1\"", "fgui.UIConfig.defaultFont=(window.__cfActiveFont||\"GoogleSansThai\")");

                var hardMap = json.Deserialize<Dictionary<string, string>>(readMeta("hard_map.json"));
                foreach (var kv in hardMap)
                {
                    if (kv.Key == "简体中文" || kv.Key == "繁體中文" || kv.Key == "繁体中文") continue;
                    string escOld = EscapeJs(kv.Key);
                    string escNew = EscapeJs(kv.Value);
                    foreach (string q in new[] { "\"", "'" })
                    {
                        foreach (string rep in new HashSet<string> { kv.Key, escOld })
                        {
                            src = src.Replace(
                                q + rep + q,
                                "(window.__isThaiLang&&window.__isThaiLang()?" + q + escNew + q + ":" + q + rep + q + ")"
                            );
                        }
                    }
                }
                foreach (var pair in new[] { Tuple.Create("点击", "คลิก"), Tuple.Create("点击交互", "คลิกเพื่อโต้ตอบ") })
                {
                    string eo = EscapeJs(pair.Item1), en = EscapeJs(pair.Item2);
                    src = Regex.Replace(
                        src,
                        @"(tipsText\?[^:;]{1,180}:)""" + Regex.Escape(eo) + "\"",
                        m => m.Groups[1].Value + "(window.__isThaiLang&&window.__isThaiLang()?\"" + en + "\":\"" + eo + "\")"
                    );
                }

                src = ReplaceFirst(src, "case\"ExitGame\":s.default.GameStateManager.exitGame()", "case\"ExitGame\":s.default.GameStateManager.exitGame(),(s.default.PlatformManager.doExit?s.default.PlatformManager.doExit():window.close())");
                src = ReplaceFirst(src, "t.prototype._updateSubtitleText=function(e){", "t.prototype._updateSubtitleText=function(e){this.subtitle&&!this.subtitle.__isSubtitle&&(this.subtitle.__isSubtitle=!0,this.subtitle.y-=32);this.subtitle_r&&!this.subtitle_r.__isSubtitle&&(this.subtitle_r.__isSubtitle=!0,this.subtitle_r.y-=32);");
                src += "\n;(function(){window.__thRefreshAllFgui=function(){try{if(!window.fgui||!fgui.GRoot||!fgui.GRoot.inst)return;var visit=function(g){if(!g)return;if(g instanceof fgui.GTextField&&typeof g.updateText==='function')g.updateText();if(g._children){for(var i=0;i<g._children.length;i++)visit(g._children[i]);}};visit(fgui.GRoot.inst);if(window.__cfRefreshDockText){window.__cfRefreshDockText();}if(window.__thRefreshLocalizedTextures){window.__thRefreshLocalizedTextures();}if(window.__cfRefreshCocosFonts&&window.__cfActiveFont){window.__cfRefreshCocosFonts(window.__cfActiveFont);}}catch(e){}};})();\n";
            }
            else
            {
                if (!src.Contains("__thPatched"))
                {
                    src = ReplaceFirst(
                        src,
                        "window.__undertoneI18n=this;",
                        "window.__undertoneI18n=this;if(window.cc&&cc.assetManager&&cc.assetManager.downloader&&!cc.assetManager.downloader.__thPatched){cc.assetManager.downloader.__thPatched=!0;var _odl=cc.assetManager.downloader.download;cc.assetManager.downloader.download=function(a,b,c,d,e){if(\"string\"==typeof b&&window.__remapThaiImageUrl)b=window.__remapThaiImageUrl(b);return _odl.call(this,a,b,c,d,e)}}"
                    );
                }
                if (!src.Contains("t._videoPath.replace(/\\.mp4$/i"))
                {
                    src = ReplaceFirst(src, "t._chapterId+\"_\"+ +t._stageLink/100+\"_\"+n+t._subtitleName}return t._subtitleName}", "t._chapterId+\"_\"+ +t._stageLink/100+\"_\"+n+t._subtitleName}return t._subtitleName||(window.__isThaiLang&&window.__isThaiLang()&&t._videoPath?t._videoPath.replace(/\\.mp4$/i,\"\"):\"\")}");
                }
                if (!src.Contains("s.default.PlatformManager.doExit"))
                {
                    src = ReplaceFirst(src, "case\"ExitGame\":s.default.GameStateManager.exitGame()", "case\"ExitGame\":s.default.GameStateManager.exitGame(),(s.default.PlatformManager.doExit?s.default.PlatformManager.doExit():window.close())");
                }
                if (!src.Contains("this.subtitle.y-=32"))
                {
                    src = src.Replace("this.subtitle&&(this.subtitle.__isSubtitle=!0);this.subtitle_r&&(this.subtitle_r.__isSubtitle=!0);", "this.subtitle&&!this.subtitle.__isSubtitle&&(this.subtitle.__isSubtitle=!0,this.subtitle.y-=32);this.subtitle_r&&!this.subtitle_r.__isSubtitle&&(this.subtitle_r.__isSubtitle=!0,this.subtitle_r.y-=32);");
                }
            }
            result[indexJsPath] = Encoding.UTF8.GetBytes(src);
        }

        return result;
    }
}
