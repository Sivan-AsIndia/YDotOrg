import { ComponentFixture, TestBed } from '@angular/core/testing';

import { TostepVerify } from './tostep-verify';

describe('TostepVerify', () => {
  let component: TostepVerify;
  let fixture: ComponentFixture<TostepVerify>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TostepVerify],
    }).compileComponents();

    fixture = TestBed.createComponent(TostepVerify);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
