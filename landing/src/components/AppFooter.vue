<script setup lang="ts">
import { t } from '../i18n'

const EMAIL = 'pjh85336@gmail.com'
</script>

<template>
  <footer class="footer">
    <div class="container">
      <div class="row">
        <!-- GitHub sits WITH THE NAME, not on a line of its own further down.
             Below, it landed next to the TabStick pill and read as TabStick's
             repository - two outbound links side by side, and the nearer label
             wins (2026-08-15). Beside "Edgetree" there is nothing else it could
             belong to. -->
        <div class="brand">
          <img src="/icon.png" alt="" width="20" height="20" />
          <span>Edgetree</span>
          <a class="repo" href="https://github.com/legendsteel11/Edgetree" target="_blank" rel="noopener">{{ t.nav.github }}</a>
        </div>
        <!-- TWO ROUTES, and the second one is why this changed (2026-08-19).
             The only way in used to be mail, and a report that arrives as mail
             arrives among the rest of the mail: an issue sat unanswered for a
             day because a small "1" in a repository header is not something
             anyone watches. An issue carries its own badge and its own
             notification, and the next person with the same symptom can read
             it instead of asking again.

             STILL IN THE FOOTER. It belongs where someone who has decided to
             use the app goes looking, not beside the download button - a line
             about bug reports there is a reason to hesitate at the moment of
             deciding. -->
        <span class="contact">
          {{ t.footer.contact }}:
          <a :href="`mailto:${EMAIL}`">{{ EMAIL }}</a>
          ·
          <a href="https://github.com/legendsteel11/Edgetree/issues" target="_blank" rel="noopener">GitHub Issues</a>
        </span>
      </div>

      <!-- 상호 링크는 푸터에(TabStick 랜딩과 같은 자리). Since 2026-08-23 the
           LABEL sits outside as plain text and each tool is a capsule of its
           own (사용자 요청) - a label locked inside the first pill made that
           pill longer, so the row read as unequal rather than as a list. That
           is what lets the row take a third (DeskNoise, 2026-08-29) and a
           fourth (MagicLoupe, 2026-09-16) without being redrawn.

           EACH LINK GOES TO A LANDING WHERE ONE EXISTS, and to the repository
           only until then: SweepCap moved off its repo on 2026-08-31, the day
           sweepcap.vercel.app went up, the way DeskNoise already had. Somebody
           following a link out of a landing page expects another one, not a
           file list. The app's own 앱 정보 carries the same four links
           (AboutWindow.xaml) and is updated with the release that follows,
           since it ships inside the exe. -->
      <p class="others">
        <span class="others-label">{{ t.footer.otherTool }}</span>
        <a class="other" href="https://tabstick.com/" target="_blank" rel="noopener">{{ t.footer.otherToolName }}</a>
        <a class="other" href="https://sweepcap.vercel.app/" target="_blank" rel="noopener">{{ t.footer.otherTool2Name }}</a>
        <a class="other" href="https://desk-noise.vercel.app/" target="_blank" rel="noopener">{{ t.footer.otherTool3Name }}</a>
        <a class="other" href="https://magicloupe.vercel.app/" target="_blank" rel="noopener">{{ t.footer.otherTool4Name }}</a>
      </p>

      <p class="copyright">{{ t.footer.copyright }}</p>
    </div>
  </footer>
</template>

<style scoped>
.footer {
  padding: 40px 0;
  border-top: 1px solid var(--border);
}

.row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 16px;
  margin-bottom: 16px;
}

.brand {
  display: flex;
  align-items: center;
  gap: 8px;
  font-weight: 600;
  color: var(--text-strong);
  font-size: 14px;
}

/* A plain link, not the pill the TabStick line wears: the pill is what made it
   read as one of a pair of equal outbound links. Here it is a note attached to
   the name beside it. */
.repo {
  margin-left: 6px;
  color: var(--text);
  text-decoration: none;
  font-size: 14px;
  font-weight: 400;
}

.repo:hover {
  color: var(--accent-strong);
}

/* 이 자리가 링크 하나였다가 라벨 + 링크 둘로 바뀌었으므로(2026-08-19), 색과
   밑줄은 안쪽 a가 받는다. 바깥 span에만 두면 라벨까지 링크처럼 보이고, 정작
   눌리는 두 곳은 브라우저 기본 파랑으로 돌아간다. */
.contact {
  color: var(--text);
  font-size: 14px;
}

.contact a {
  color: inherit;
  text-decoration: none;
}

.contact a:hover {
  color: var(--accent-strong);
}

/* 다른 도구 안내 줄: 라벨은 맨글자, 도구마다 옅은 파랑 틴트 알약 하나씩. */
.others {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 12px;
}

.others-label {
  font-size: 13.5px;
  color: var(--text);
  margin-right: 2px;
}

.other {
  display: inline-block;
  font-size: 13.5px;
  padding: 7px 18px;
  border: 1px solid var(--border);
  border-radius: 999px;
  background: var(--accent-bg);
  color: var(--accent-strong);
  font-weight: 700;
  text-decoration: none;
}

.other:hover {
  text-decoration: underline;
}

.copyright {
  font-size: 12.5px;
  opacity: 0.6;
}
</style>
