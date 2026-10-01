/* Kit-only loader: transpiles components/*.jsx in the browser so kit pages always render the current
   sources, then fires 'ds-ready' once. Inert anywhere else (e.g. when the compiler bundles it). */
(function(){
  var cs=document.currentScript;
  if(!cs||!/ds-fallback\.js(\?|$)/.test(cs.src||''))return; // inside _ds_bundle.js or inline: do nothing
  if(window.__dsFallbackInit)return; window.__dsFallbackInit=true;
  var NS='MediaAcquisitionManagerDS_29af91';
  var root=(cs.getAttribute('data-root')||'../../');
  var files=['core/Icon','core/Button','core/Badge','core/Banner','core/Input','core/Select','core/Card','core/Dialog','core/Menu','core/ListPager','core/AppLogo','core/ThemeSwitch','core/ErrorState','media/ChipRow','media/MatchBadges','media/ReleaseRow','media/WorkCard'];
  var names=['Icon','Button','Badge','Banner','StatusLine','Input','Field','Select','Card','Dialog','Menu','ListPager','AppLogo','ThemeSwitch','ErrorState','ChipRow','Facts','MatchBadges','ReleaseRow','WorkCard'];
  function ready(){window.dispatchEvent(new Event('ds-ready'));window.__dsReady=true;}
  window.addEventListener('load',function(){
    Promise.all(files.map(function(f){return fetch(root+'components/'+f+'.jsx').then(function(r){return r.text();});})).then(function(srcs){
      var src=srcs.map(function(t){return t.replace(/^import .*$/mg,'').replace(/^export /mg,'');}).join('\n');
      src+='\nwindow["'+NS+'"]={'+names.join(',')+'};';
      (0,eval)(Babel.transform(src,{presets:['react']}).code);ready();
    });
  });
})();
