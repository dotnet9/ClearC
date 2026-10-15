const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require('playwright');
const standaloneFixture=require('./standalone-fixture.cjs');
const output=path.resolve(process.env.CLEARC_AUDIT_OUTPUT||path.join(__dirname,'../../artifacts/prototype-audit'));
fs.mkdirSync(output,{recursive:true});
(async()=>{
  const standalone=standaloneFixture(path.resolve(__dirname,'../index.html'));
  const browser=await chromium.launch({headless:true});
  try{
    const context=await browser.newContext({viewport:{width:1360,height:900},offline:true,reducedMotion:'reduce'});
    const page=await context.newPage();await standalone.attach(page);
    const errors=[];page.on('pageerror',error=>errors.push(error.message));await page.goto(standalone.url);
    async function capture(name){
      await page.locator('#appWindow').screenshot({path:path.join(output,name+'.png'),animations:'disabled'});
      const geometry=await page.evaluate(()=>{
        const frame=document.querySelector('#appWindow').getBoundingClientRect();
        return [['window','#appWindow'],['workspace','.page'],['donut','.donut-wrap'],['list','.list-zone'],['console','.console'],['statusbar','.statusbar']].map(([key,selector])=>{const r=document.querySelector(selector).getBoundingClientRect();return [key,r.x-frame.x,r.y-frame.y,r.width,r.height].join(',');});
      });
      fs.writeFileSync(path.join(output,name+'.geometry.csv'),'key,x,y,width,height\n'+geometry.join('\n')+'\n');
    }
    const state=()=>page.locator('body').getAttribute('data-state');
    for(const culture of ['zh-CN','en-US'])for(const palette of ['DotNet','Aurora','Ember','Default','Aquatic','Desert','Dusk','NightSky'])for(const mode of ['Light','Dark']){
      await page.evaluate(change=>window.ClearCPrototype.set(change),{culture,palette,mode});
      assert.equal(await page.locator('[data-choice="culture"]').inputValue(),culture);
      assert.equal(await page.locator('[data-choice="palette"]').inputValue(),palette);
      assert.equal(await page.locator('.palette-dot.on').count(),1);
      if(culture==='en-US')assert.deepEqual(await page.evaluate(()=>{
        const walker=document.createTreeWalker(document.body,NodeFilter.SHOW_TEXT),found=[];let node;
        while(node=walker.nextNode()){const parent=node.parentElement;if(!parent||parent.closest('script,style,select,svg')||!parent.checkVisibility())continue;if(/[\u4e00-\u9fff]/.test(node.textContent))found.push(node.textContent.trim());}return found;
      }),[],'Untranslated English interface');
      await capture(`main-${culture}-${palette}-${mode}`);
    }
    await page.locator('[data-choice="mode"]').selectOption('System');assert.equal(await page.evaluate(()=>window.ClearCPrototype.getState().mode),'System');
    await page.reload();assert.equal(await page.locator('[data-choice="mode"]').inputValue(),'System');
    for(const culture of ['zh-CN','en-US']){
      await page.evaluate(change=>window.ClearCPrototype.set(change),{culture,palette:'DotNet',mode:'Light'});
      await page.locator('#btnAbout').click();assert.equal(await page.locator('#about').isVisible(),true);
      assert.equal(await page.locator('.about-captions button').count(),3);
      assert.ok((await page.locator('#about dl').innerText()).includes('2026-10-15 00:00:00'));
      assert.ok(await page.locator('#appWindow').evaluate(el=>parseFloat(getComputedStyle(el).borderTopWidth)>0));
      for(const update of ['checking','available','downloading','downloaded','canceled','failed']){
        await page.evaluate(value=>window.ClearCPrototype.setUpdate(value),update);
        assert.ok((await page.locator('#updateStatus').innerText()).length>0);
      }
      await page.locator('#aboutClose').click();
      await page.locator('#btnMin').click();assert.equal(await page.locator('#taskbar').isVisible(),true);await page.locator('#btnRestore').click();
      await page.locator('#btnMain').click();assert.equal(await state(),'scanning');await page.locator('#btnSecond').click();assert.equal(await state(),'idle');
      await page.locator('#btnMain').click();await capture(`workflow-${culture}-scanning`);
      await page.waitForFunction(()=>document.body.dataset.state==='results');
      assert.ok((await page.locator('#list').innerText()).length>50);await page.locator('.g-head').first().click();
      const checked=await page.evaluate(()=>window.ClearCPrototype.getState().checked);
      await page.evaluate(change=>window.ClearCPrototype.set(change),{culture:culture==='en-US'?'zh-CN':'en-US'});
      assert.deepEqual(await page.evaluate(()=>window.ClearCPrototype.getState().checked),checked);
      await page.evaluate(change=>window.ClearCPrototype.set(change),{culture});
      await capture(`workflow-${culture}-results`);
      await page.locator('.row[data-id]').first().click({button:'right'});assert.equal(await page.locator('#ctxMenu').isVisible(),true);await page.locator('#ctxMenu button').first().click();
      await page.locator('#btnMain').click();assert.equal(await page.locator('#mask').isVisible(),true);await capture(`workflow-${culture}-confirming`);
      await page.keyboard.press('Escape');assert.equal(await page.locator('#mask').isVisible(),false);
      await page.locator('#btnMain').click();await page.locator('#mActions button').last().click();assert.equal(await state(),'cleaning');await capture(`workflow-${culture}-cleaning`);
      await page.waitForFunction(()=>document.body.dataset.state==='done');await capture(`workflow-${culture}-done`);
      const download=page.waitForEvent('download');await page.locator('#btnExportLog').click();assert.equal((await download).suggestedFilename(),'ClearC.log');
      await page.locator('#btnClose').click();assert.equal(await page.locator('#relaunch').isVisible(),true);await page.locator('#btnRelaunch').click();assert.equal(await state(),'idle');
    }
    await standalone.verify(page);assert.deepEqual(errors,[]);
    console.log('PASS ClearC: 32 appearance/language combinations, live language changes, persisted preferences, 6 workflow views, selection, context menu, cancel, confirmation, log export, about, 6 update states and offline relaunch.');
  }finally{await browser.close();standalone.dispose();}
})().catch(error=>{console.error(error);process.exitCode=1;});
