"use strict";(self.webpackChunkmidjourney_proxy_admin=self.webpackChunkmidjourney_proxy_admin||[]).push([[201],{2891:function(oa,be,l){l.r(be),l.d(be,{default:function(){return sa}});var ye=l(15009),M=l.n(ye),we=l(99289),W=l.n(we),ke=l(5574),m=l.n(ke),G=l(66927),oe=l(16568),Se=l(86738),P=l(14726),Te=l(82061),d=l(67294),ne=l(80854),e=l(85893),Ae=function(x){var I=x.record,i=x.onSuccess,t=(0,d.useState)(!1),s=m()(t,2),F=s[0],y=s[1],J=(0,d.useState)(!1),E=m()(J,2),N=E[0],R=E[1],j=oe.ZP.useNotification(),A=m()(j,2),O=A[0],U=A[1],k=(0,ne.useIntl)(),B=function(){y(!0)},$=function(){var Z=W()(M()().mark(function v(){var S;return M()().wrap(function(r){for(;;)switch(r.prev=r.next){case 0:return R(!0),r.prev=1,r.next=4,(0,G.tm)(I.id);case 4:S=r.sent,y(!1),S.success?(O.success({message:"success",description:k.formatMessage({id:"pages.account.deleteSuccess"})}),i()):O.error({message:"error",description:S.message}),r.next=12;break;case 9:r.prev=9,r.t0=r.catch(1),console.error(r.t0);case 12:return r.prev=12,R(!1),r.finish(12);case 15:case"end":return r.stop()}},v,null,[[1,9,12,15]])}));return function(){return Z.apply(this,arguments)}}(),X=function(){y(!1)};return(0,e.jsxs)(Se.Z,{title:k.formatMessage({id:"pages.account.delete"}),description:k.formatMessage({id:"pages.account.deleteTitle"}),open:F,onConfirm:$,okButtonProps:{loading:N},onCancel:X,children:[U,(0,e.jsx)(P.ZP,{danger:!0,icon:(0,e.jsx)(Te.Z,{}),onClick:B})]})},Oe=Ae,de=l(98165),Re=function(x){var I=x.record,i=x.onSuccess,t=(0,d.useState)(!1),s=m()(t,2),F=s[0],y=s[1],J=(0,d.useState)(!1),E=m()(J,2),N=E[0],R=E[1],j=oe.ZP.useNotification(),A=m()(j,2),O=A[0],U=A[1],k=(0,ne.useIntl)(),B=function(){y(!0)},$=function(){var Z=W()(M()().mark(function v(){var S;return M()().wrap(function(r){for(;;)switch(r.prev=r.next){case 0:return R(!0),r.prev=1,r.next=4,(0,G.Je)(I.id);case 4:S=r.sent,console.log("resss",S),y(!1),S.success?(O.success({message:"success",description:k.formatMessage({id:"pages.account.syncSuccess"})}),i()):(O.error({message:"error",description:S.message}),i()),r.next=13;break;case 10:r.prev=10,r.t0=r.catch(1),console.error(r.t0);case 13:return r.prev=13,R(!1),r.finish(13);case 16:case"end":return r.stop()}},v,null,[[1,10,13,16]])}));return function(){return Z.apply(this,arguments)}}(),X=function(){y(!1)};return(0,e.jsxs)(Se.Z,{title:k.formatMessage({id:"pages.account.sync"}),description:k.formatMessage({id:"pages.account.syncTitle"}),open:F,onConfirm:$,okButtonProps:{loading:N},onCancel:X,children:[U,(0,e.jsx)(P.ZP,{icon:(0,e.jsx)(de.Z,{}),onClick:B})]})},Be=Re,a=l(53025),me=l(71230),ee=l(15746),H=l(4393),u=l(55102),z=l(72269),w=l(74656),b=l(37804),re=l(42075),je=l(28248),Ze=l(40056),Ce=l(5785),De=function(x){var I=x.form,i=x.onSubmit,t=(0,ne.useIntl)();(0,d.useEffect)(function(){I.setFieldsValue({userAgent:"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36",coreSize:3,queueSize:10,timeoutMinutes:5})});var s=(0,d.useState)([]),F=m()(s,2),y=F[0],J=F[1];(0,d.useEffect)(function(){(0,G.fl)().then(function(Z){Z.success&&J(Z.data)})},[]);var E=(0,d.useState)(!1),N=m()(E,2),R=N[0],j=N[1],A=(0,d.useState)(""),O=m()(A,2),U=O[0],k=O[1],B=function(){j(!0)},$=function(){I.setFieldsValue({subChannels:U.split(`
`)}),j(!1)},X=function(){j(!1)};return(0,e.jsxs)(a.Z,{form:I,labelAlign:"left",layout:"horizontal",labelCol:{span:8},wrapperCol:{span:16},onFinish:i,children:[(0,e.jsxs)(me.Z,{gutter:16,children:[(0,e.jsx)(ee.Z,{span:8,children:(0,e.jsxs)(H.Z,{type:"inner",title:t.formatMessage({id:"pages.account.info"}),children:[(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.guildId"}),name:"guildId",rules:[{required:!0}],children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.channelId"}),name:"channelId",rules:[{required:!0}],children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.userToken"}),name:"userToken",rules:[{required:!0}],children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.botToken"}),name:"botToken",children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.mjChannelId"}),name:"privateChannelId",children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.nijiChannelId"}),name:"nijiBotChannelId",children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:"User Agent",name:"userAgent",children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.enable"}),name:"enable",valuePropName:"checked",children:(0,e.jsx)(z.Z,{})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.remixAutoSubmit"}),name:"remixAutoSubmit",valuePropName:"checked",children:(0,e.jsx)(z.Z,{})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.mode"}),name:"mode",children:(0,e.jsxs)(w.Z,{allowClear:!0,children:[(0,e.jsx)(w.Z.Option,{value:"RELAX",children:"RELAX"}),(0,e.jsx)(w.Z.Option,{value:"FAST",children:"FAST"}),(0,e.jsx)(w.Z.Option,{value:"TURBO",children:"TURBO"})]})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.allowModes"}),name:"allowModes",children:(0,e.jsxs)(w.Z,{allowClear:!0,mode:"multiple",children:[(0,e.jsx)(w.Z.Option,{value:"RELAX",children:"RELAX"}),(0,e.jsx)(w.Z.Option,{value:"FAST",children:"FAST"}),(0,e.jsx)(w.Z.Option,{value:"TURBO",children:"TURBO"})]})})]})}),(0,e.jsx)(ee.Z,{span:8,children:(0,e.jsxs)(H.Z,{type:"inner",title:t.formatMessage({id:"pages.account.poolsize"}),children:[(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.coreSize"}),name:"coreSize",children:(0,e.jsx)(b.Z,{min:1})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.queueSize"}),name:"queueSize",children:(0,e.jsx)(b.Z,{min:1})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.maxQueueSize"}),name:"maxQueueSize",children:(0,e.jsx)(b.Z,{min:1})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.interval"}),name:"interval",children:(0,e.jsx)(b.Z,{min:1.2})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.intervalAfter"}),children:(0,e.jsx)("div",{style:{display:"flex",flexDirection:"row",alignItems:"center"},children:(0,e.jsxs)(re.Z,{children:[(0,e.jsx)(a.Z.Item,{name:"afterIntervalMin",style:{margin:0},children:(0,e.jsx)(b.Z,{min:1.2,placeholder:"Min"})}),"~",(0,e.jsx)(a.Z.Item,{name:"afterIntervalMax",style:{margin:0},children:(0,e.jsx)(b.Z,{min:1.2,placeholder:"Max"})})]})})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.weight"}),name:"weight",children:(0,e.jsx)(b.Z,{min:1})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.enableMj"}),name:"enableMj",children:(0,e.jsx)(z.Z,{})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.enableNiji"}),name:"enableNiji",children:(0,e.jsx)(z.Z,{})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.isBlend"}),name:"isBlend",children:(0,e.jsx)(z.Z,{})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.isDescribe"}),name:"isDescribe",children:(0,e.jsx)(z.Z,{})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.dayDrawLimit"}),name:"dayDrawLimit",children:(0,e.jsx)(b.Z,{min:-1})})]})}),(0,e.jsx)(ee.Z,{span:8,children:(0,e.jsxs)(H.Z,{type:"inner",title:t.formatMessage({id:"pages.account.otherInfo"}),children:[(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.permanentInvitationLink"}),name:"permanentInvitationLink",children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.isVerticalDomain"}),name:"isVerticalDomain",children:(0,e.jsx)(z.Z,{})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.verticalDomainIds"}),name:"verticalDomainIds",children:(0,e.jsx)(w.Z,{options:y,allowClear:!0,mode:"multiple"})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.sort"}),name:"sort",children:(0,e.jsx)(b.Z,{})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.timeoutMinutes"}),name:"timeoutMinutes",children:(0,e.jsx)(b.Z,{min:1,suffix:t.formatMessage({id:"pages.minutes"})})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.sponsor"}),name:"sponsor",children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.remark"}),name:"remark",children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.workTime"}),name:"workTime",children:(0,e.jsx)(u.Z,{placeholder:"09:00-17:00, 18:00-22:00"})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.fishingTime"}),help:t.formatMessage({id:"pages.account.fishingTimeTips"}),name:"fishingTime",children:(0,e.jsx)(u.Z,{placeholder:"23:30-09:00, 00:00-10:00"})}),(0,e.jsx)(a.Z.Item,{label:t.formatMessage({id:"pages.account.subChannels"}),name:"subChannels",extra:(0,e.jsx)(P.ZP,{type:"primary",style:{marginTop:"10px"},onClick:function(){B()},icon:(0,e.jsx)(Ce.Z,{})}),children:(0,e.jsx)(u.Z.TextArea,{disabled:!0,autoSize:{minRows:1,maxRows:5}})})]})})]}),(0,e.jsxs)(je.Z,{title:t.formatMessage({id:"pages.account.subChannels"}),visible:R,onOk:$,onCancel:X,width:960,children:[(0,e.jsx)("div",{children:(0,e.jsx)(Ze.Z,{message:t.formatMessage({id:"pages.account.subChannelsHelp"}),type:"info",style:{marginBottom:"10px"}})}),(0,e.jsx)(u.Z.TextArea,{placeholder:"https://discord.com/channels/xxx/xxx",autoSize:{minRows:10,maxRows:24},style:{width:"100%"},value:U,onChange:function(v){k(v.target.value)}})]})]})},Fe=De,Ne=function(x){var I=x.form,i=x.onSubmit,t=x.record,s=(0,ne.useIntl)(),F=oe.ZP.useNotification(),y=m()(F,2),J=y[0],E=y[1],N=(0,d.useState)(),R=m()(N,2),j=R[0],A=R[1],O=(0,d.useState)(!1),U=m()(O,2),k=U[0],B=U[1];(0,d.useEffect)(function(){A(t)},[]);var $=function(){var Z=W()(M()().mark(function v(){var S;return M()().wrap(function(r){for(;;)switch(r.prev=r.next){case 0:return B(!0),r.next=3,(0,G.qG)(t.id);case 3:S=r.sent,B(!1),S.success&&A(S.data);case 6:case"end":return r.stop()}},v)}));return function(){return Z.apply(this,arguments)}}(),X=function(){var Z=W()(M()().mark(function v(){var S;return M()().wrap(function(r){for(;;)switch(r.prev=r.next){case 0:return B(!0),r.next=3,(0,G.eD)(t.id);case 3:S=r.sent,B(!1),S.success&&(J.success({message:"success",description:"Success"}),i(j));case 6:case"end":return r.stop()}},v)}));return function(){return Z.apply(this,arguments)}}();return(0,e.jsxs)(a.Z,{form:I,labelAlign:"left",layout:"horizontal",labelCol:{span:8},wrapperCol:{span:16},onFinish:i,children:[E,(0,e.jsx)(me.Z,{gutter:16,children:(0,e.jsxs)(ee.Z,{span:24,style:{padding:12},children:[j&&(0,e.jsx)("a",{target:"_blank",rel:"noreferrer",href:j==null?void 0:j.cfUrl,children:(j==null?void 0:j.cfUrl)||"-"}),(0,e.jsx)("br",{}),(0,e.jsxs)(re.Z,{style:{marginTop:12},children:[(0,e.jsx)(P.ZP,{onClick:$,loading:k,type:"default",children:s.formatMessage({id:"pages.account.cfRefresh"})}),(0,e.jsx)(P.ZP,{onClick:X,loading:k,type:"primary",children:s.formatMessage({id:"pages.account.cfok"})})]})]})})]})},Ue=Ne,le=l(66309),ce=l(83062),g=l(26412),Le=l(27484),Pe=l.n(Le),ze=w.Z.Option,Ee=function(x){var I,i=x.record,t=x.onSuccess,s=oe.ZP.useNotification(),F=m()(s,2),y=F[0],J=F[1],E=(0,d.useState)(""),N=m()(E,2),R=N[0],j=N[1],A=(0,d.useState)([]),O=m()(A,2),U=O[0],k=O[1],B=(0,d.useState)([]),$=m()(B,2),X=$[0],Z=$[1],v=(0,d.useState)(!1),S=m()(v,2),Q=S[0],r=S[1],ge=(0,d.useState)(""),ae=m()(ge,2),ue=ae[0],fe=ae[1],o=(0,ne.useIntl)();(0,d.useEffect)(function(){j(i.version),k(i.buttons),Z(i.nijiButtons)},[i]);var pe=function(c,T,q){var _=c?"green":"volcano",L=c?T:q;return(0,e.jsx)(le.Z,{color:_,children:L})},Y=function(c){return!c||c.length<25?c:(0,e.jsx)(ce.Z,{title:c,children:c.substring(0,25)+"..."})},f=function(c){return Pe()(c).format("YYYY-MM-DD HH:mm")},ve=function(){var c;return(c=i.versionSelector)===null||c===void 0?void 0:c.map(function(T){return(0,e.jsxs)(ze,{value:T.customId,children:[T.emoji," ",T.label]},T.customId)})},he=function(){return U.map(function(c){return(0,e.jsxs)(P.ZP,{ghost:!0,style:{backgroundColor:c.style==3?"#258146":"rgb(131 133 142)"},onClick:function(){xe("MID_JOURNEY",c.customId)},loading:ue=="MID_JOURNEY:"+c.customId,children:[c.emoji," ",c.label]},"MID_JOURNEY:"+c.customId)})},Me=function(){return X.map(function(c){return(0,e.jsxs)(P.ZP,{ghost:!0,style:{backgroundColor:c.style==3?"#258146":"rgb(131 133 142)"},onClick:function(){xe("NIJI_JOURNEY",c.customId)},loading:ue=="NIJI_JOURNEY:"+c.customId,children:[c.emoji," ",c.label]},"NIJI_JOURNEY:"+c.customId)})},Ie=function(){var K=W()(M()().mark(function c(T){var q;return M()().wrap(function(L){for(;;)switch(L.prev=L.next){case 0:return j(T),r(!0),L.next=4,(0,G.p3)(i.id,T);case 4:q=L.sent,q.success?(r(!1),y.success({message:"success",description:o.formatMessage({id:"pages.account.mjVersionSuccess"})}),t()):(j(i.version),r(!1),y.error({message:"error",description:q.message}));case 6:case"end":return L.stop()}},c)}));return function(T){return K.apply(this,arguments)}}(),xe=function(){var K=W()(M()().mark(function c(T,q){var _;return M()().wrap(function(se){for(;;)switch(se.prev=se.next){case 0:if(ue===""){se.next=2;break}return se.abrupt("return");case 2:return fe(T+":"+q),se.next=5,(0,G.wO)(i.id,T,q);case 5:_=se.sent,fe(""),_.success?t():y.error({message:"error",description:_.message});case 8:case"end":return se.stop()}},c)}));return function(T,q){return K.apply(this,arguments)}}();return(0,e.jsxs)(e.Fragment,{children:[J,(0,e.jsx)(H.Z,{type:"inner",title:o.formatMessage({id:"pages.account.info"}),style:{margin:"5px"},children:(0,e.jsxs)(g.Z,{column:3,children:[(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.guildId"}),children:i.guildId}),(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.channelId"}),children:i.channelId}),(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.userToken"}),children:Y(i.userToken)}),(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.botToken"}),children:Y(i.botToken)}),(0,e.jsxs)(g.Z.Item,{label:"User Agent",children:[Y(i.userAgent)," "]}),(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.remixAutoSubmit"}),children:pe(i.remixAutoSubmit,o.formatMessage({id:"pages.yes"}),o.formatMessage({id:"pages.no"}))}),(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.mjChannelId"}),children:i.privateChannelId}),(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.nijiChannelId"}),children:i.nijiBotChannelId})]})}),(0,e.jsx)(H.Z,{type:"inner",title:o.formatMessage({id:"pages.account.basicInfo"}),style:{margin:"5px"},children:(0,e.jsxs)(g.Z,{column:3,children:[(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.status"}),children:pe(i.enable,o.formatMessage({id:"pages.enable"}),o.formatMessage({id:"pages.disable"}))}),(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.mjMode"}),children:i.displays.mode}),(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.nijiMode"}),children:i.displays.nijiMode}),(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.subscribePlan"}),children:i.displays.subscribePlan}),(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.billedWay"}),children:i.displays.billedWay}),(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.renewDate"}),children:i.displays.renewDate}),(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.fastTimeRemaining"}),children:i.fastTimeRemaining}),(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.relaxedUsage"}),children:i.relaxedUsage}),(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.fastUsage"}),children:i.fastUsage}),(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.turboUsage"}),children:i.turboUsage}),(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.lifetimeUsage"}),children:i.lifetimeUsage})]})}),(0,e.jsx)(H.Z,{type:"inner",title:o.formatMessage({id:"pages.account.otherInfo"}),style:{margin:"5px"},children:(0,e.jsxs)(g.Z,{column:3,children:[(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.coreSize"}),children:i.coreSize}),(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.queueSize"}),children:i.queueSize}),(0,e.jsxs)(g.Z.Item,{label:o.formatMessage({id:"pages.account.timeoutMinutes"}),children:[i.timeoutMinutes," ",o.formatMessage({id:"pages.minutes"})]}),(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.weight"}),children:i.weight}),(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.dateCreated"}),children:f(i.dateCreated)}),(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.remark"}),children:Y(i.remark)}),(0,e.jsx)(g.Z.Item,{label:o.formatMessage({id:"pages.account.disabledReason"}),children:Y(i.disabledReason)})]})}),(0,e.jsxs)(H.Z,{type:"inner",title:o.formatMessage({id:"pages.account.mjSettings"}),style:{margin:"5px"},children:[(0,e.jsx)(w.Z,{style:{width:"35%"},placeholder:(I=i.versionSelector)===null||I===void 0?void 0:I.placeholder,value:R,onChange:Ie,loading:Q,children:ve()}),(0,e.jsx)(re.Z,{wrap:!0,style:{marginTop:"5px"},children:he()})]}),(0,e.jsx)(H.Z,{type:"inner",title:o.formatMessage({id:"pages.account.nijiSettings"}),style:{margin:"5px"},children:(0,e.jsx)(re.Z,{wrap:!0,style:{marginTop:"5px"},children:Me()})})]})},$e=Ee,Ve=function(x){var I=x.form,i=x.onSubmit,t=x.record,s=(0,ne.useIntl)();(0,d.useEffect)(function(){I.setFieldsValue(t)});var F=(0,d.useState)([]),y=m()(F,2),J=y[0],E=y[1];(0,d.useEffect)(function(){(0,G.fl)().then(function(v){v.success&&E(v.data)})},[]);var N=(0,d.useState)(!1),R=m()(N,2),j=R[0],A=R[1],O=(0,d.useState)(""),U=m()(O,2),k=U[0],B=U[1],$=function(){A(!0)},X=function(){t.subChannels=k.split(`
`),I.setFieldsValue({subChannels:k.split(`
`)}),A(!1)},Z=function(){A(!1)};return(0,e.jsxs)(a.Z,{form:I,labelAlign:"left",layout:"horizontal",labelCol:{span:8},wrapperCol:{span:16},onFinish:i,children:[(0,e.jsxs)(me.Z,{gutter:16,children:[(0,e.jsx)(ee.Z,{span:8,children:(0,e.jsxs)(H.Z,{type:"inner",title:s.formatMessage({id:"pages.account.info"}),children:[(0,e.jsx)(a.Z.Item,{label:"id",name:"id",hidden:!0,children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.guildId"}),name:"guildId",rules:[{required:!0}],children:(0,e.jsx)(u.Z,{disabled:!0})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.channelId"}),name:"channelId",rules:[{required:!0}],children:(0,e.jsx)(u.Z,{disabled:!0})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.userToken"}),name:"userToken",rules:[{required:!0}],children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.botToken"}),name:"botToken",children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.mjChannelId"}),name:"privateChannelId",children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.nijiChannelId"}),name:"nijiBotChannelId",children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:"User Agent",name:"userAgent",children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.enable"}),name:"enable",valuePropName:"checked",children:(0,e.jsx)(z.Z,{})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.remixAutoSubmit"}),name:"remixAutoSubmit",valuePropName:"checked",children:(0,e.jsx)(z.Z,{})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.mode"}),name:"mode",children:(0,e.jsxs)(w.Z,{allowClear:!0,children:[(0,e.jsx)(w.Z.Option,{value:"RELAX",children:"RELAX"}),(0,e.jsx)(w.Z.Option,{value:"FAST",children:"FAST"}),(0,e.jsx)(w.Z.Option,{value:"TURBO",children:"TURBO"})]})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.allowModes"}),name:"allowModes",tooltip:"\u5982\u679C\u7528\u6237\u6307\u5B9A\u6A21\u5F0F\u6216\u6DFB\u52A0\u4E86\u81EA\u5B9A\u4E49\u53C2\u6570\u4F8B\u5982 --fast\uFF0C\u4F46\u662F\u8D26\u53F7\u4E0D\u5141\u8BB8 FAST\uFF0C\u5219\u81EA\u52A8\u79FB\u9664\u6B64\u53C2\u6570",children:(0,e.jsxs)(w.Z,{allowClear:!0,mode:"multiple",children:[(0,e.jsx)(w.Z.Option,{value:"RELAX",children:"RELAX"}),(0,e.jsx)(w.Z.Option,{value:"FAST",children:"FAST"}),(0,e.jsx)(w.Z.Option,{value:"TURBO",children:"TURBO"})]})})]})}),(0,e.jsx)(ee.Z,{span:8,children:(0,e.jsxs)(H.Z,{type:"inner",title:s.formatMessage({id:"pages.account.poolsize"}),children:[(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.coreSize"}),name:"coreSize",children:(0,e.jsx)(b.Z,{min:1})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.queueSize"}),name:"queueSize",children:(0,e.jsx)(b.Z,{min:1})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.maxQueueSize"}),name:"maxQueueSize",children:(0,e.jsx)(b.Z,{min:1})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.interval"}),name:"interval",children:(0,e.jsx)(b.Z,{min:1.2})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.intervalAfter"}),children:(0,e.jsx)("div",{style:{display:"flex",flexDirection:"row",alignItems:"center"},children:(0,e.jsxs)(re.Z,{children:[(0,e.jsx)(a.Z.Item,{name:"afterIntervalMin",style:{margin:0},children:(0,e.jsx)(b.Z,{min:1.2,placeholder:"Min"})}),"~",(0,e.jsx)(a.Z.Item,{name:"afterIntervalMax",style:{margin:0},children:(0,e.jsx)(b.Z,{min:1.2,placeholder:"Max"})})]})})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.weight"}),name:"weight",children:(0,e.jsx)(b.Z,{min:1})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.enableMj"}),name:"enableMj",children:(0,e.jsx)(z.Z,{})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.enableNiji"}),name:"enableNiji",children:(0,e.jsx)(z.Z,{})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.isBlend"}),name:"isBlend",children:(0,e.jsx)(z.Z,{})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.isDescribe"}),name:"isDescribe",children:(0,e.jsx)(z.Z,{})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.dayDrawLimit"}),name:"dayDrawLimit",extra:t.dayDrawCount>0&&(0,e.jsxs)("span",{children:[s.formatMessage({id:"pages.account.dayDrawCount"})," ",t.dayDrawCount]}),children:(0,e.jsx)(b.Z,{min:-1})})]})}),(0,e.jsx)(ee.Z,{span:8,children:(0,e.jsxs)(H.Z,{type:"inner",title:s.formatMessage({id:"pages.account.otherInfo"}),children:[(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.permanentInvitationLink"}),name:"permanentInvitationLink",children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.isVerticalDomain"}),name:"isVerticalDomain",children:(0,e.jsx)(z.Z,{})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.verticalDomainIds"}),name:"verticalDomainIds",children:(0,e.jsx)(w.Z,{options:J,allowClear:!0,mode:"multiple"})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.sort"}),name:"sort",children:(0,e.jsx)(b.Z,{})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.timeoutMinutes"}),name:"timeoutMinutes",children:(0,e.jsx)(b.Z,{min:1,suffix:s.formatMessage({id:"pages.minutes"})})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.sponsor"}),name:"sponsor",children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.remark"}),name:"remark",children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.workTime"}),name:"workTime",children:(0,e.jsx)(u.Z,{placeholder:"09:00-17:00, 18:00-22:00"})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.fishingTime"}),help:s.formatMessage({id:"pages.account.fishingTimeTips"}),name:"fishingTime",children:(0,e.jsx)(u.Z,{placeholder:"23:30-09:00, 00:00-10:00"})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.subChannels"}),name:"subChannels",extra:(0,e.jsx)(P.ZP,{type:"primary",style:{marginTop:"10px"},onClick:function(){B(I.getFieldValue("subChannels").join(`
`)),$()},icon:(0,e.jsx)(Ce.Z,{})}),children:(0,e.jsx)(u.Z.TextArea,{disabled:!0,autoSize:{minRows:1,maxRows:5}})})]})})]}),(0,e.jsxs)(je.Z,{title:s.formatMessage({id:"pages.account.subChannels"}),visible:j,onOk:X,onCancel:Z,width:960,children:[(0,e.jsx)("div",{children:(0,e.jsx)(Ze.Z,{message:s.formatMessage({id:"pages.account.subChannelsHelp"}),type:"info",style:{marginBottom:"10px"}})}),(0,e.jsx)(u.Z.TextArea,{placeholder:"https://discord.com/channels/xxx/xxx",autoSize:{minRows:10,maxRows:24},style:{width:"100%"},value:k,onChange:function(S){B(S.target.value)}})]}),(0,e.jsx)(Ze.Z,{message:s.formatMessage({id:"pages.account.updateNotice"}),type:"warning",style:{marginTop:"10px"}})]})},He=Ve,Je=function(x){var I=x.form,i=x.onSubmit,t=x.record,s=(0,ne.useIntl)();return(0,d.useEffect)(function(){I.setFieldsValue(t)}),(0,e.jsx)(a.Z,{form:I,labelAlign:"left",layout:"horizontal",labelCol:{span:8},wrapperCol:{span:16},onFinish:i,children:(0,e.jsxs)(me.Z,{gutter:16,children:[(0,e.jsx)(ee.Z,{span:12,children:(0,e.jsxs)(H.Z,{type:"inner",title:s.formatMessage({id:"pages.account.info"}),children:[(0,e.jsx)(a.Z.Item,{label:"id",name:"id",hidden:!0,children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.mjChannelId"}),name:"privateChannelId",children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.nijiChannelId"}),name:"nijiBotChannelId",children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.remixAutoSubmit"}),name:"remixAutoSubmit",valuePropName:"checked",children:(0,e.jsx)(z.Z,{})})]})}),(0,e.jsx)(ee.Z,{span:12,children:(0,e.jsxs)(H.Z,{type:"inner",title:s.formatMessage({id:"pages.account.otherInfo"}),children:[(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.timeoutMinutes"}),name:"timeoutMinutes",children:(0,e.jsx)(b.Z,{min:1,suffix:s.formatMessage({id:"pages.minutes"})})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.weight"}),name:"weight",children:(0,e.jsx)(b.Z,{min:1})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.remark"}),name:"remark",children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.sponsor"}),name:"sponsor",children:(0,e.jsx)(u.Z,{})}),(0,e.jsx)(a.Z.Item,{label:s.formatMessage({id:"pages.account.sort"}),name:"sort",children:(0,e.jsx)(b.Z,{})})]})})]})})},Ye=Je,Xe=l(94149),We=l(66212),Ge=l(3355),Qe=l(2830),Ke=l(47389),qe=l(88360),_e=l(90930),ea=l(72051),aa=function(){var x=(0,d.useState)(!1),I=m()(x,2),i=I[0],t=I[1],s=(0,d.useState)(!1),F=m()(s,2),y=F[0],J=F[1],E=(0,d.useState)((0,e.jsx)(e.Fragment,{})),N=m()(E,2),R=N[0],j=N[1],A=(0,d.useState)(""),O=m()(A,2),U=O[0],k=O[1],B=(0,d.useState)(1e3),$=m()(B,2),X=$[0],Z=$[1],v=a.Z.useForm(),S=m()(v,1),Q=S[0],r=oe.ZP.useNotification(),ge=m()(r,2),ae=ge[0],ue=ge[1],fe=(0,d.useState)(!1),o=m()(fe,2),pe=o[0],Y=o[1],f=(0,ne.useIntl)(),ve=(0,d.useState)([]),he=m()(ve,2),Me=he[0],Ie=he[1],xe=(0,d.useState)(!1),K=m()(xe,2),c=K[0],T=K[1],q=function(){var C=W()(M()().mark(function h(){var n;return M()().wrap(function(V){for(;;)switch(V.prev=V.next){case 0:return T(!0),V.next=3,(0,G.Ed)();case 3:n=V.sent,Ie(n),T(!1);case 6:case"end":return V.stop()}},h)}));return function(){return C.apply(this,arguments)}}(),_=function(h,n,p){Q.resetFields(),k(h),j(n),Z(p),t(!0)},L=function(){j((0,e.jsx)(e.Fragment,{})),t(!1),J(!1)},se=(0,e.jsxs)(e.Fragment,{children:[(0,e.jsx)(P.ZP,{onClick:L,children:f.formatMessage({id:"pages.cancel"})},"back"),(0,e.jsx)(P.ZP,{type:"primary",loading:pe,onClick:function(){return Q.submit()},children:f.formatMessage({id:"pages.submit"})},"submit")]}),te=function(){L(),q()},na=function(){var C=W()(M()().mark(function h(n){var p;return M()().wrap(function(D){for(;;)switch(D.prev=D.next){case 0:return n.subChannels||(n.subChannels=[]),typeof n.subChannels=="string"&&(n.subChannels=n.subChannels.split(`
`)),Y(!0),D.next=5,(0,G.o1)(n);case 5:p=D.sent,Y(!1),console.log("res",p),p.success?(ae.success({message:"success",description:p.message}),L(),te()):ae.error({message:"error",description:p.message});case 9:case"end":return D.stop()}},h)}));return function(n){return C.apply(this,arguments)}}(),ta=function(){var C=W()(M()().mark(function h(n){var p;return M()().wrap(function(D){for(;;)switch(D.prev=D.next){case 0:return n.subChannels||(n.subChannels=[]),typeof n.subChannels=="string"&&(n.subChannels=n.subChannels.split(`
`)),Y(!0),D.next=5,(0,G.MS)(n.id,n);case 5:p=D.sent,p.success?(ae.success({message:"success",description:p.message}),L(),te(),Y(!1)):ae.error({message:"error",description:p.message});case 7:case"end":return D.stop()}},h)}));return function(n){return C.apply(this,arguments)}}(),ia=function(){var C=W()(M()().mark(function h(n){var p;return M()().wrap(function(D){for(;;)switch(D.prev=D.next){case 0:return n.subChannels||(n.subChannels=[]),typeof n.subChannels=="string"&&(n.subChannels=n.subChannels.split(`
`)),Y(!0),D.next=5,(0,G.Vx)(n.id,n);case 5:p=D.sent,p.success?ae.success({message:"success",description:p.message}):ae.error({message:"error",description:p.message}),L(),te(),Y(!1);case 10:case"end":return D.stop()}},h)}));return function(n){return C.apply(this,arguments)}}(),ra=function(){var C=W()(M()().mark(function h(){return M()().wrap(function(p){for(;;)switch(p.prev=p.next){case 0:Y(!0),L(),te(),Y(!1);case 4:case"end":return p.stop()}},h)}));return function(){return C.apply(this,arguments)}}(),la=[{title:f.formatMessage({id:"pages.account.guildId"}),dataIndex:"guildId",width:200,align:"center",render:function(h,n){return(0,e.jsx)("a",{onClick:function(){J(!0),_(f.formatMessage({id:"pages.account.info"})+" - "+n.id,(0,e.jsx)($e,{record:n,onSuccess:te}),1100)},children:h})}},{title:f.formatMessage({id:"pages.account.channelId"}),dataIndex:"channelId",align:"center",width:200},{title:"".concat(f.formatMessage({id:"pages.account.status"})),dataIndex:"enable",width:200,align:"center",request:function(){var C=W()(M()().mark(function n(){return M()().wrap(function(V){for(;;)switch(V.prev=V.next){case 0:return V.abrupt("return",[{label:f.formatMessage({id:"pages.enable"}),value:"true"},{label:f.formatMessage({id:"pages.disable"}),value:"false"}]);case 1:case"end":return V.stop()}},n)}));function h(){return C.apply(this,arguments)}return h}(),render:function(h,n){var p=h?"green":"volcano",V=h?f.formatMessage({id:"pages.enable"}):f.formatMessage({id:"pages.disable"});return(0,e.jsxs)(e.Fragment,{children:[(0,e.jsx)(le.Z,{color:p,children:V}),n.lock&&(0,e.jsx)(le.Z,{icon:(0,e.jsx)(Xe.Z,{}),color:"warning",children:(0,e.jsx)(ce.Z,{title:f.formatMessage({id:"pages.account.lockmsg"}),children:f.formatMessage({id:"pages.account.lock"})})}),h&&!n.running&&!n.lock&&(0,e.jsx)(le.Z,{icon:(0,e.jsx)(de.Z,{}),color:"error",children:f.formatMessage({id:"pages.account.notRunning"})}),(n==null?void 0:n.runningCount)>0&&(0,e.jsx)(le.Z,{icon:(0,e.jsx)(de.Z,{spin:!0}),color:"cyan",children:(0,e.jsx)(ce.Z,{title:f.formatMessage({id:"pages.account.runningCount"})+" "+n.runningCount,children:(n==null?void 0:n.runningCount)||0})}),(n==null?void 0:n.queueCount)>0&&(0,e.jsx)(le.Z,{icon:(0,e.jsx)(We.Z,{}),color:"processing",children:(0,e.jsx)(ce.Z,{title:f.formatMessage({id:"pages.account.queueCount"})+" "+n.queueCount,children:n.queueCount||0})})]})}},{title:f.formatMessage({id:"pages.account.fastTimeRemaining"}),dataIndex:"fastTimeRemaining",ellipsis:!0,width:200,hideInSearch:!0,align:"center"},{title:f.formatMessage({id:"pages.account.renewDate"}),dataIndex:"renewDate",align:"center",width:160,hideInSearch:!0,render:function(h,n){return n.displays.renewDate}},{title:f.formatMessage({id:"pages.account.sponsor"}),dataIndex:"sponsor",ellipsis:!0,width:100,render:function(h,n){return(0,e.jsx)("div",{dangerouslySetInnerHTML:{__html:n.sponsor||"-"}})}},{title:f.formatMessage({id:"pages.account.remark"}),dataIndex:"remark",ellipsis:!0,width:150},{title:f.formatMessage({id:"pages.account.disabledReason"}),dataIndex:"disabledReason",ellipsis:!0,width:150,hideInSearch:!0},{title:f.formatMessage({id:"pages.operation"}),dataIndex:"operation",width:220,key:"operation",fixed:"right",align:"center",hideInSearch:!0,render:function(h,n){return(0,e.jsxs)(re.Z,{children:[n.lock&&(0,e.jsx)(P.ZP,{icon:(0,e.jsx)(Ge.Z,{}),type:"dashed",onClick:function(){return _(f.formatMessage({id:"pages.account.cfmodal"}),(0,e.jsx)(Ue,{form:Q,record:n,onSubmit:ra}),1e3)}},"Lock"),(0,e.jsx)(Be,{record:n,onSuccess:te}),(0,e.jsx)(ce.Z,{title:f.formatMessage({id:"pages.account.updateAndReconnect"}),children:(0,e.jsx)(P.ZP,{type:"primary",icon:(0,e.jsx)(Qe.Z,{}),onClick:function(){return _(f.formatMessage({id:"pages.account.updateAndReconnect"}),(0,e.jsx)(He,{form:Q,record:n,onSubmit:ta}),1600)}},"EditAndReconnect")}),(0,e.jsx)(P.ZP,{icon:(0,e.jsx)(Ke.Z,{}),onClick:function(){return _(f.formatMessage({id:"pages.account.update"}),(0,e.jsx)(Ye,{form:Q,record:n,onSubmit:ia}),1e3)}},"Update"),(0,e.jsx)(Oe,{record:n,onSuccess:te})]})}}];return(0,d.useEffect)(function(){q()},[]),(0,e.jsxs)(_e._z,{children:[ue,(0,e.jsxs)(H.Z,{children:[(0,e.jsxs)("div",{style:{display:"flex",justifyContent:"flex-end",marginBottom:16},children:[(0,e.jsx)(P.ZP,{type:"primary",icon:(0,e.jsx)(qe.Z,{}),onClick:function(){_(f.formatMessage({id:"pages.account.add"}),(0,e.jsx)(Fe,{form:Q,onSubmit:na}),1600)},children:f.formatMessage({id:"pages.add"})},"primary"),(0,e.jsx)(P.ZP,{type:"default",style:{marginLeft:8},icon:(0,e.jsx)(de.Z,{}),onClick:function(){te()}})]}),(0,e.jsx)(ea.Z,{scroll:{x:1e3},rowKey:"id",columns:la,dataSource:Me,loading:c,pagination:!1})]}),(0,e.jsx)(je.Z,{title:U,open:i,onCancel:L,footer:y?null:se,width:X,children:R})]})},sa=aa}}]);